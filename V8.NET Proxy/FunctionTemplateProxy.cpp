#include "ProxyTypes.h"

// ------------------------------------------------------------------------------------------------------------------------

FunctionTemplateProxy::FunctionTemplateProxy(V8EngineProxy* engineProxy, uint16_t* className, ManagedJSFunctionCallback managedCallback)
    :ProxyBase(FunctionTemplateProxyClass), _EngineProxy(engineProxy), _EngineID(engineProxy->_EngineID)
{
    // The function template will call the local "InvocationCallbackProxy" function, which then translates the call for the managed side.
    _FunctionTemplate = Persistent<FunctionTemplate>::New(_EngineProxy->Isolate(), FunctionTemplate::New(InvocationCallbackProxy, External::New(this)));
    _FunctionTemplate->SetClassName(String::New(className));

    _InstanceTemplate = new ObjectTemplateProxy(_EngineProxy, _FunctionTemplate->InstanceTemplate());
    _PrototypeTemplate = new ObjectTemplateProxy(_EngineProxy, _FunctionTemplate->PrototypeTemplate());

    SetManagedCallback(managedCallback);
}

FunctionTemplateProxy::~FunctionTemplateProxy()
{
    // Note: the '_InstanceTemplate' and '_PrototypeTemplate' instances are not deleted because the managed GC will do that later.
    _InstanceTemplate = nullptr;
    _PrototypeTemplate = nullptr;

    if (!V8EngineProxy::IsDisposed(_EngineID))
    {
        BEGIN_ISOLATE_SCOPE(_EngineProxy);
        BEGIN_CONTEXT_SCOPE(_EngineProxy);

        if (!_FunctionTemplate.IsEmpty())
            _FunctionTemplate.Dispose();

        END_CONTEXT_SCOPE;
        END_ISOLATE_SCOPE;
    }

    _EngineProxy = nullptr;
}

// ------------------------------------------------------------------------------------------------------------------------

void FunctionTemplateProxy::SetManagedCallback(ManagedJSFunctionCallback managedCallback) { _ManagedCallback = managedCallback; }

// ------------------------------------------------------------------------------------------------------------------------

Handle<Value> FunctionTemplateProxy::InvocationCallbackProxy(const Arguments& args)
{
    auto funcTempProxy = (FunctionTemplateProxy*)args.Data().As<External>()->Value();

    if (funcTempProxy->_ManagedCallback != nullptr)
    {
        auto argLength = args.Length();
        auto _args = argLength > 0 ? new HandleProxy*[argLength] : nullptr;

        for (auto i = 0; i < argLength; i++)
            _args[i] = funcTempProxy->_EngineProxy->GetHandleProxy(args[i]);

        auto _this = funcTempProxy->_EngineProxy->GetHandleProxy(args.Holder());

        auto result = funcTempProxy->_ManagedCallback(0, args.IsConstructCall(), _this, _args, argLength);

        if (result != nullptr)
            if (result->IsError())
                return ThrowException(Exception::Error(result->Handle()->ToString()));
            else
                return result->Handle(); // (note: the returned value was created via p/invoke calls from the managed side, so the managed side is expected to tracked and freed this handle when done)

        // (result == null == undefined [which means the managed side didn't return anything])
    }

    return Handle<Value>();
}

// ------------------------------------------------------------------------------------------------------------------------

ObjectTemplateProxy* FunctionTemplateProxy::GetInstanceTemplateProxy()
{
    return _InstanceTemplate;
}

// ------------------------------------------------------------------------------------------------------------------------

ObjectTemplateProxy* FunctionTemplateProxy::GetPrototypeTemplateProxy()
{
    return _PrototypeTemplate;
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy* FunctionTemplateProxy::GetFunction()
{
    auto obj = _FunctionTemplate->GetFunction();
    auto proxyVal = _EngineProxy->GetHandleProxy(obj);
    return proxyVal;
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy* FunctionTemplateProxy::CreateInstance(int32_t managedObjectID, int32_t argCount, HandleProxy** args)
{
    Handle<Value>* hArgs = new Handle<Value>[argCount];
    for (int i = 0; i < argCount; i++)
        hArgs[i] = args[i]->Handle();
    auto obj = _FunctionTemplate->GetFunction()->NewInstance(argCount, hArgs);
    delete[] hArgs; // TODO: (does "disposed" still need to be called here for each item?)

    auto proxyVal = _EngineProxy->GetHandleProxy(obj);
    proxyVal->_ManagedObjectID = managedObjectID;
    //??auto count = obj->InternalFieldCount();
    obj->SetAlignedPointerInInternalField(0, this); // (stored a reference to the proxy instance for the call-back functions)
    obj->SetInternalField(1, External::New((void*)(intptr_t)managedObjectID)); // (stored a reference to the managed object for the call-back functions)
    obj->SetHiddenValue(String::New("ManagedObjectID"), Integer::New(managedObjectID)); // (won't be used on template created objects [fields are faster], but done anyhow for consistency)
    return proxyVal;
}

// ------------------------------------------------------------------------------------------------------------------------
