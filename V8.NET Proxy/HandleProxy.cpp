#include "ProxyTypes.h"

// ------------------------------------------------------------------------------------------------------------------------

Handle<Value> HandleProxy::Handle() { return _Handle; }

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy::HandleProxy(V8EngineProxy* engineProxy, int32_t id)
    : _Type((JSValueType)-1), _ID(id), _ManagedReferenceCount(0), _ManagedObjectID(-1), __EngineProxy(0)
{
    _EngineProxy = engineProxy;
    _EngineID = _EngineProxy->_EngineID;
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy::~HandleProxy()
{
    _Dispose(false);
    _Value.Dispose();
}

// Sets the state if this instance to disposed (for safety, the handle is NOT disposed.
// (registerDisposal is false when called within 'V8EngineProxy.DisposeHandleProxy()' (to prevent a cyclical loop), or by the engine's destructor)
bool HandleProxy::_Dispose(bool registerDisposal)
{
    std::lock_guard<std::recursive_mutex>(_EngineProxy->_HandleSystemMutex); // NO V8 HANDLE ACCESS HERE BECAUSE OF THE MANAGED GC

    if (V8EngineProxy::IsDisposed(_EngineID))
        delete this; // (the engine is gone, so just destroy the memory [the managed side owns UNDISPOSED proxy handles - they are not deleted with the engine)
    else
        if (_Disposed == 1)
        {
            if (registerDisposal)
            {
                _EngineProxy->DisposeHandleProxy(this);
                return true;
            }

            _ManagedObjectID = -1;
            _Value.Dispose();
            _Disposed = 2;

            return true;
        };;

    return false; // (already disposed, or engine is gone)
}

bool HandleProxy::Dispose()
{
    return _Dispose(true);
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy* HandleProxy::Initialize(v8::Handle<Value> handle)
{
    if (_Disposed > 0) _Dispose(false); // (just resets whatever is needed)

    SetHandle(handle);

    _Disposed = 0;

    return this;
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy* HandleProxy::SetHandle(v8::Handle<Value> handle)
{
    if (!_Handle.IsEmpty())
    {
        _Handle.Dispose();
        _Handle.Clear();
    }

    _Handle = Persistent<Value>::New(handle);

    if (_Handle->IsBoolean())
    {
        _Type = JSV_Bool;
    }
    else if (_Handle->IsBooleanObject()) // TODO: Validate this is correct.
    {
        _Type = JSV_BoolObject;
    }
    else if (_Handle->IsInt32())
    {
        _Type = JSV_Int32;
    }
    else if (_Handle->IsNumber())
    {
        _Type = JSV_Number;
    }
    else if (_Handle->IsNumberObject()) // TODO: Validate this is correct.
    {
        _Type = JSV_NumberObject;
    }
    else if (_Handle->IsString())
    {
        _Type = JSV_String;
    }
    else if (_Handle->IsStringObject())// TODO: Validate this is correct.
    {
        _Type = JSV_StringObject;
    }
    else if (_Handle->IsDate())
    {
        _Type = JSV_Date;
    }
    else if (_Handle->IsArray())
    {
        _Type = JSV_Array;
    }
    else if (_Handle->IsRegExp())
    {
        _Type = JSV_RegExp;
    }
    else if (_Handle->IsNull())
    {
        _Type = JSV_Object;
    }
    else if (_Handle->IsFunction())
    {
        _Type = JSV_Function;
    }
    else if (_Handle->IsExternal())
    {
        _Type = JSV_Undefined;
    }
    else if (_Handle->IsNativeError())
    {
        _Type = JSV_Undefined;
    }
    else if (_Handle->IsUndefined())
    {
        _Type = JSV_Undefined;
    }
    else if (_Handle->IsObject()) // WARNING: Do this AFTER any possible object type checks (example: creating functions makes this return true as well!!!)
    {
        _Type = JSV_Object;
    }
    else if (_Handle->IsFalse()) // TODO: Validate this is correct.
    {
        _Type = JSV_Bool;
    }
    else if (_Handle->IsTrue()) // TODO: Validate this is correct.
    {
        _Type = JSV_Bool;
    }
    else
    {
        _Type = JSV_Undefined;
    }

    return this;
}

void HandleProxy::_DisposeCallback(Isolate* isolate, Persistent<Value> object, void* parameter)
{
    //auto engineProxy = (V8EngineProxy*)isolate->GetData();
    //auto handleProxy = parameter;
    object.Dispose();
}

// ------------------------------------------------------------------------------------------------------------------------

// Should be called once to attempt to pull the ID.
// If there's no ID, then the managed object ID will be set to -2 to prevent checking again.
// To force a re-check, simply set the value back to -1.
int HandleProxy::GetManagedObjectID()
{
    if (_ManagedObjectID < -1 || _ManagedObjectID >= 0)
        return _ManagedObjectID;
    else {
        _ManagedObjectID = -2; // (assume nothing found until something is)

        if (_Handle->IsObject())
        {
            // ... if this was created by a template then there will be at least 2 fields set, so assume the second is a managed ID value, 
            // but if not, then check for a hidden property for objects not created by templates ...

            auto obj = _Handle.As<Object>();

            if (obj->InternalFieldCount() > 1)
            {
                auto field = obj->GetInternalField(1); // (may be faster than hidden values)
                if (field->IsExternal())
                    _ManagedObjectID = (int32_t)field.As<External>()->Value();
            }
            else
            {
                auto handle = obj->GetHiddenValue(String::New("ManagedObjectID"));
                if (!handle.IsEmpty() && handle->IsInt32())
                    _ManagedObjectID = (int32_t)handle->Int32Value();
            }
        }
    }
    return _ManagedObjectID;
}

// ------------------------------------------------------------------------------------------------------------------------

// This is called when the managed side is ready to destroy the V8 handle.
void HandleProxy::MakeWeak()
{
    _Handle.MakeWeak<Value, HandleProxy>(_EngineProxy->_Isolate, this, _RevivableCallback);
}

// This is called when the managed side is no longer ready to destroy this V8 handle.
void HandleProxy::MakeStrong()
{
    _Handle.ClearWeak(_EngineProxy->_Isolate);
}

// ------------------------------------------------------------------------------------------------------------------------
// When the managed side is ready to destroy a handle, it first marks it as weak.  When the V8 engine's garbage collector finally calls back, the managed side
// object information is finally destroyed.

void HandleProxy::_RevivableCallback(Isolate* isolate, Persistent<Value>* object, HandleProxy* parameter)
{
    auto engineProxy = (V8EngineProxy*)isolate->GetData();
    auto handleProxy = parameter;

    auto dispose = true;

    if (engineProxy->_ManagedV8GarbageCollectionRequestCallback != nullptr)
    {
        if (handleProxy->_ManagedObjectID >= 0)
            dispose = engineProxy->_ManagedV8GarbageCollectionRequestCallback(handleProxy);
    }

    if (dispose) // (Note: the managed callback may have already cached the handle, but the handle value will not be disposed yet)
    {
        if (!handleProxy->_Handle.IsEmpty())
        {
            handleProxy->_Handle.Dispose(); // (V8 handle is no longer tracked on the managed side, so let it go within this GC request [better here while idle])
            handleProxy->_Handle.Clear(); // (no longer valid)
        }
    }
}

// ------------------------------------------------------------------------------------------------------------------------

void HandleProxy::UpdateValue()
{
    _Value.Dispose();

    switch (_Type)
    {
        // (note: ';' is prepended to prevent Visual Studio from formatting "switch..case" statements in a retarded manner (at least in VS2012!) )
        ;case JSV_Bool:
        {
            _Value.V8Boolean = _Handle->BooleanValue(); 
            break;
        }
        ;case JSV_BoolObject:
        {
            _Value.V8Boolean = _Handle->BooleanValue(); 
            break;
        }
        ;case JSV_Int32:
        {
            _Value.V8Integer = _Handle->Int32Value(); 
            break;
        }
        ;case JSV_Number:
        {
            _Value.V8Number = _Handle->NumberValue(); 
            break;
        }
        ;case JSV_NumberObject:
        {
            _Value.V8Number = _Handle->NumberValue();
            break;
        }
        ;case JSV_String:
        {
            _Value.V8String = _StringItem(_EngineProxy, *_Handle.As<String>()).String; 
            break;
        }
        ;case JSV_StringObject:
        {
            _Value.V8String = _StringItem(_EngineProxy, *_Handle.As<String>()).String; 
            break;
        }
        ;case JSV_Date:
        {
            _Value.V8Number = _Handle->NumberValue(); 
            _Value.V8String = _StringItem(_EngineProxy, *_Handle.As<String>()).String; 
            break;
        }
        ;case JSV_Undefined:
        {
            _Value.V8Number = 0; // (make sure this is cleared just in case...)
            break;
        }
        ;default: // (by default, an "object" type is assumed (warning: this includes functions); however, we can't translate it (obviously), so we just return a reference to this handle proxy instead)
        {
            if (!_Handle.IsEmpty())
                _Value.V8String = _StringItem(_EngineProxy, *_Handle->ToString()).String; 
            break;
        }
    }
}

// ------------------------------------------------------------------------------------------------------------------------
