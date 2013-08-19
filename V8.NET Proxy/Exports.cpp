#include "ProxyTypes.h"

// ############################################################################################################################
// Misc. Global Functions

Handle<Value> _GetObjectProperty(Local<String> hName, const AccessorInfo& info) // (for non-ObjectTemplate objects!)
{
    auto obj = info.Holder();

    if (!obj.IsEmpty())
    {
        auto hID = obj->GetHiddenValue(String::New("ManagedObjectID"));
        auto hGetter = obj->GetHiddenValue(String::New("_Getter_"));
        if (!hID->IsUndefined() && hGetter->IsExternal())
        {
            auto engine = (V8EngineProxy*)info.GetIsolate()->GetData();
            auto managedObjectID = (int32_t)hID->Int32Value();
            ManagedAccessorGetter getter = (ManagedAccessorGetter)hGetter.As<External>()->Value();

            if (managedObjectID >= 0 && getter != nullptr)
            {
                auto _this = engine->GetHandleProxy(info.This());
                auto str = engine->GetNativeString(*hName);
                auto result = getter(_this, str.String); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();
                if (result != nullptr) return result->Handle(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return v8::Undefined();
}

// ------------------------------------------------------------------------------------------------------------------------

void _SetObjectProperty(Local<String> hName, Local<Value> value, const AccessorInfo& info) // (for non-ObjectTemplate objects!)
{
    auto obj = info.Holder();

    if (!obj.IsEmpty())
    {
        auto hID = obj->GetHiddenValue(String::New("ManagedObjectID"));
        auto hSetter = obj->GetHiddenValue(String::New("_Setter_"));
        if (!hID->IsUndefined() && hSetter->IsExternal())
        {
            auto engine = (V8EngineProxy*)info.GetIsolate()->GetData();
            auto managedObjectID = (int32_t)hID->Int32Value();
            ManagedAccessorSetter setter = (ManagedAccessorSetter)hSetter.As<External>()->Value();

            if (managedObjectID >= 0 && setter != nullptr)
            {
                auto _this = engine->GetHandleProxy(info.This());
                auto str = engine->GetNativeString(*hName);
                auto _value = engine->GetHandleProxy(value);
                setter(_this, str.String, _value); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();
            }
        }
    }
}

// ############################################################################################################################
// DLL Exports

// Prevent name mangling for the interface functions. 
extern "C"
{
    // ------------------------------------------------------------------------------------------------------------------------
    // Engine Related

    EXPORT V8EngineProxy* STDCALL CreateV8EngineProxy(bool enableDebugging, DebugMessageDispatcher *debugMessageDispatcher, int debugPort)
    { return new V8EngineProxy(enableDebugging, debugMessageDispatcher, debugPort); }
    EXPORT void STDCALL DestroyV8EngineProxy(V8EngineProxy *engine) { delete engine; }

    EXPORT void STDCALL WithV8IsolateScope(V8EngineProxy *engine, CallbackAction action) { engine->WithIsolateScope(action); }
    EXPORT void STDCALL WithV8ContextScope(V8EngineProxy *engine, CallbackAction action) { engine->WithContextScope(action); }
    EXPORT void STDCALL WithHandleScope(V8EngineProxy *engine, CallbackAction action) { engine->WithHandleScope(action); }

    EXPORT void STDCALL RegisterGCCallback(V8EngineProxy* engine, ManagedV8GarbageCollectionRequestCallback managedV8GarbageCollectionRequestCallback)
    { engine->RegisterGCCallback(managedV8GarbageCollectionRequestCallback); }

    EXPORT void STDCALL ForceGC(V8EngineProxy* engine)
    {
        V8::LowMemoryNotification();
        while(!V8::IdleNotification())
        {}
    }

    EXPORT bool STDCALL DoIdleNotification(V8EngineProxy* engine, int hint = 1000)
    {
        return V8::IdleNotification(hint);
    }

    EXPORT HandleProxy* STDCALL V8Execute(V8EngineProxy *engine, uint16_t *script, uint16_t *sourceName) { return engine->Execute(script, sourceName); }

    // ------------------------------------------------------------------------------------------------------------------------
    // Object Template Related

    EXPORT ObjectTemplateProxy* STDCALL CreateObjectTemplateProxy(V8EngineProxy *engine) { return engine->CreateObjectTemplate(); }
    EXPORT void STDCALL DeleteObjectTemplateProxy(ObjectTemplateProxy *proxy) { delete proxy; }
    EXPORT HandleProxy* STDCALL SetGlobalObjectTemplate(V8EngineProxy *engine, ObjectTemplateProxy *proxy) { return engine->SetGlobalObjectTemplate(proxy); } 

    EXPORT void STDCALL RegisterNamedPropertyHandlers(ObjectTemplateProxy *proxy,
        ManagedNamedPropertyGetter getter, 
        ManagedNamedPropertySetter setter, 
        ManagedNamedPropertyQuery query, 
        ManagedNamedPropertyDeleter deleter, 
        ManagedNamedPropertyEnumerator enumerator)
    { proxy->RegisterNamedPropertyHandlers(getter, setter, query, deleter, enumerator); }

    EXPORT void STDCALL RegisterIndexedPropertyHandlers(ObjectTemplateProxy *proxy,
        ManagedIndexedPropertyGetter getter, 
        ManagedIndexedPropertySetter setter, 
        ManagedIndexedPropertyQuery query, 
        ManagedIndexedPropertyDeleter deleter, 
        ManagedIndexedPropertyEnumerator enumerator)
    { proxy->RegisterIndexedPropertyHandlers(getter, setter, query, deleter, enumerator); }

    EXPORT void STDCALL UnregisterNamedPropertyHandlers(ObjectTemplateProxy *proxy)
    { proxy->UnregisterNamedPropertyHandlers(); }

    EXPORT void STDCALL UnregisterIndexedPropertyHandlers(ObjectTemplateProxy *proxy)
    { proxy->UnregisterIndexedPropertyHandlers(); }

    EXPORT HandleProxy* STDCALL CreateObjectFromTemplate(ObjectTemplateProxy *proxy, int32_t managedObjectID) { return proxy->CreateObject(managedObjectID); }

    // This function connects objects that are created internally by V8, but are based on custom templates (such as new instances created by functions where V8
    // creates the object internally and passes it along).
    // 'templateProxy' should be null (for basic non-template objects), or a reference to one of the native proxy template classes.
    EXPORT void STDCALL ConnectObject(HandleProxy *handleProxy, int32_t managedObjectID, void* templateProxy)
    {
        auto handle = handleProxy->Handle();
        if (!handle.IsEmpty() && handle->IsObject())
        {
            auto obj = handleProxy->Handle().As<Object>();
            if (obj->InternalFieldCount() > 1)
            {
                if (templateProxy != nullptr)
                    obj->SetAlignedPointerInInternalField(0, templateProxy); // (stored a reference to the proxy instance for the call-back function(s))
                obj->SetInternalField(1, External::New((void*)managedObjectID));
            }
            obj->SetHiddenValue(String::New("ManagedObjectID"), Integer::New(managedObjectID)); // (won't be used on template created objects [fields are faster], but done anyhow for consistency)
        }
        handleProxy->SetManagedObjectID(managedObjectID);
    }

    EXPORT HandleProxy* STDCALL GetObjectPrototype(HandleProxy *handleProxy)
    {
        auto handle = handleProxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        return handleProxy->EngineProxy()->GetHandleProxy(handle.As<Object>()->GetPrototype());
    }

    EXPORT HandleProxy* STDCALL Call(HandleProxy *subject, const uint16_t *functionName, HandleProxy *_this, uint16_t argCount, HandleProxy** args)
    {
        if (_this == nullptr) _this = subject; // (assume the subject is also "this" if not given)

        auto hThis = _this->Handle();
        if (hThis.IsEmpty() || !hThis->IsObject())
            throw exception("Call: The target instance handle ('this') does not represent an object.");

        auto hSubject = subject->Handle();
        Handle<Function> hFunc;

        if (functionName != nullptr) // (if no name is given, assume the subject IS a function object, otherwise get the property as a function)
        {
            if (hSubject.IsEmpty() || !hSubject->IsObject())
                throw exception("Call: The subject handle does not represent an object.");

            auto hProp = hSubject.As<Object>()->Get(String::New(functionName));

            if (hProp.IsEmpty() || !hProp->IsFunction())
                throw exception("Call: The specified property does not represent a function.");

            hFunc = hProp.As<Function>();
        }
        else if (hSubject.IsEmpty() || !hSubject->IsFunction())
            throw exception("Call: The subject handle does not represent a function.");
        else
            hFunc = hSubject.As<Function>();

        Handle<Value> result;

        if (argCount > 0)
        {
            Handle<Value>* _args = new Handle<Value>[argCount];
            for (auto i = 0; i < argCount; i++)
                _args[i] = args[i]->Handle();
            result = hFunc->Call(hThis.As<Object>(), argCount, _args);
            delete[] _args;
        }
        else result = hFunc->Call(hThis.As<Object>(), 0, nullptr);

        return result.IsEmpty() ? nullptr : subject->EngineProxy()->GetHandleProxy(result);
    }

    // ------------------------------------------------------------------------------------------------------------------------

    EXPORT bool STDCALL SetObjectPropertyByName(HandleProxy *proxy, const uint16_t *name, HandleProxy *value, v8::PropertyAttribute attribs = None)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        Handle<Value> valueHandle = value != nullptr ? value->Handle() : v8::Undefined();
        return obj->Set(String::New(name), valueHandle, attribs);
    }

    EXPORT bool STDCALL SetObjectPropertyByIndex(HandleProxy *proxy, const uint16_t index, HandleProxy *value)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        Handle<Value> valueHandle = value != nullptr ? value->Handle() : v8::Undefined();
        return obj->Set(index,  valueHandle);
    }

    EXPORT HandleProxy* STDCALL GetObjectPropertyByName(HandleProxy *proxy, const uint16_t *name)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        return proxy->EngineProxy()->GetHandleProxy(obj->Get(String::New(name)));
    }

    EXPORT HandleProxy* STDCALL GetObjectPropertyByIndex(HandleProxy *proxy, const uint16_t index)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        return proxy->EngineProxy()->GetHandleProxy(obj->Get(index));
    }

    EXPORT bool STDCALL DeleteObjectPropertyByName(HandleProxy *proxy, const uint16_t *name)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        return obj->Delete(String::New(name));
    }

    EXPORT bool STDCALL DeleteObjectPropertyByIndex(HandleProxy *proxy, const uint16_t index)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        return obj->Delete(index);
    }

    EXPORT void STDCALL SetObjectAccessor(HandleProxy *proxy, int32_t managedObjectID, const uint16_t *name,
        ManagedAccessorGetter getter, ManagedAccessorSetter setter,
        v8::AccessControl access, v8::PropertyAttribute attributes)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        obj->SetHiddenValue(String::New("ManagedObjectID"), Integer::New(managedObjectID));
        //??obj->SetHiddenValue(String::New("_HandleProxy_"), External::New(proxy));
        obj->SetHiddenValue(String::New("_Getter_"), External::New(getter));
        obj->SetHiddenValue(String::New("_Setter_"), External::New(setter));
        obj->SetAccessor(String::New(name), _GetObjectProperty, _SetObjectProperty, v8::Null(), access, attributes);  // TODO: Check how this affects objects created from templates!
    }

    EXPORT HandleProxy* STDCALL GetPropertyNames(HandleProxy *proxy)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        auto names = obj->GetPropertyNames();
        return proxy->EngineProxy()->GetHandleProxy(names);
    }

    EXPORT HandleProxy* STDCALL GetOwnPropertyNames(HandleProxy *proxy)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        auto names = obj->GetOwnPropertyNames();
        return proxy->EngineProxy()->GetHandleProxy(names);
    }

    EXPORT PropertyAttribute STDCALL GetPropertyAttributes(HandleProxy *proxy, const uint16_t * name)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsObject())
            throw exception("The handle does not represent an object.");
        auto obj = handle.As<Object>();
        return obj->GetPropertyAttributes(String::New(name));
    }

    EXPORT int32_t STDCALL GetArrayLength(HandleProxy *proxy)
    {
        auto handle = proxy->Handle();
        if (handle.IsEmpty() || !handle->IsArray())
            throw exception("The handle does not represent an array object.");
        return handle.As<Array>()->Length();
    }

    // ------------------------------------------------------------------------------------------------------------------------
    // Function Template Related
    EXPORT FunctionTemplateProxy* STDCALL CreateFunctionTemplateProxy(V8EngineProxy *engine, uint16_t *className, ManagedJSFunctionCallback callback)
    { return engine->CreateFunctionTemplate(className, callback); }
    EXPORT void STDCALL DeleteFunctionTemplateProxy(FunctionTemplateProxy *proxy) { delete proxy; }
    EXPORT ObjectTemplateProxy* STDCALL GetFunctionInstanceTemplateProxy(FunctionTemplateProxy *proxy) { return proxy->GetInstanceTemplateProxy(); }
    EXPORT ObjectTemplateProxy* STDCALL GetFunctionPrototypeTemplateProxy(FunctionTemplateProxy *proxy) { return proxy->GetPrototypeTemplateProxy(); }
    //??EXPORT void STDCALL SetManagedJSFunctionCallback(FunctionTemplateProxy *proxy, ManagedJSFunctionCallback callback)  { proxy->SetManagedCallback(callback); }

    EXPORT HandleProxy* STDCALL GetFunction(FunctionTemplateProxy *proxy) { return proxy->GetFunction(); }
    //??EXPORT HandleProxy* STDCALL GetFunctionPrototype(FunctionTemplateProxy *proxy, int32_t managedObjectID, ObjectTemplateProxy *objTemplate)
    //??{ return proxy->GetPrototype(managedObjectID, objTemplate); }
    EXPORT HandleProxy* STDCALL CreateFunctionInstance(FunctionTemplateProxy *proxy, int32_t managedObjectID, int32_t argCount = 0, HandleProxy** args = nullptr)
    { return proxy->CreateInstance(managedObjectID, argCount, args); }

    // ------------------------------------------------------------------------------------------------------------------------
    // Value Creation 

    EXPORT HandleProxy* STDCALL CreateBoolean(V8EngineProxy *engine, bool b) { return engine->CreateBoolean(b); }
    EXPORT HandleProxy* STDCALL CreateInteger(V8EngineProxy *engine, int32_t num) { return engine->CreateInteger(num); }
    EXPORT HandleProxy* STDCALL CreateNumber(V8EngineProxy *engine, double num) { return engine->CreateNumber(num); }
    EXPORT HandleProxy* STDCALL CreateString(V8EngineProxy *engine, uint16_t* str) { return engine->CreateString(str); }
    EXPORT HandleProxy* STDCALL CreateDate(V8EngineProxy *engine, double ms) { return engine->CreateDate(ms); }
    EXPORT HandleProxy* STDCALL CreateObject(V8EngineProxy *engine, int32_t managedObjectID) { return engine->CreateObject(managedObjectID); }
    EXPORT HandleProxy* STDCALL CreateArray(V8EngineProxy *engine, HandleProxy** items, uint16_t length) { return engine->CreateArray(items, length); }
    EXPORT HandleProxy* STDCALL CreateStringArray(V8EngineProxy *engine, uint16_t **items, uint16_t length) { return engine->CreateArray(items, length); }

    // ------------------------------------------------------------------------------------------------------------------------
    // Handle Related

    EXPORT void STDCALL MakeWeakHandle(HandleProxy *handleProxy) { if (handleProxy != nullptr) handleProxy->MakeWeak(); }
    EXPORT void STDCALL MakeStrongHandle(HandleProxy *handleProxy) { if (handleProxy != nullptr) handleProxy->MakeStrong(); }

    EXPORT void STDCALL DisposeHandleProxy(HandleProxy *handleProxy) { if (handleProxy != nullptr) handleProxy->Dispose(); }

    EXPORT void STDCALL UpdateHandleValue(HandleProxy *handleProxy) { if (handleProxy != nullptr) handleProxy->UpdateValue(); }
    EXPORT int STDCALL GetHandleManagedObjectID(HandleProxy *handleProxy) { if (handleProxy != nullptr) return handleProxy->GetManagedObjectID(); else return -1; }

    // ------------------------------------------------------------------------------------------------------------------------
}

// ############################################################################################################################
