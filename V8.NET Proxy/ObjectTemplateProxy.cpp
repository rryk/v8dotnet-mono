#include "ProxyTypes.h"

// ------------------------------------------------------------------------------------------------------------------------

ObjectTemplateProxy::ObjectTemplateProxy(V8EngineProxy* engineProxy)
    :ProxyBase(ObjectTemplateProxyClass), _EngineProxy(engineProxy), _EngineID(engineProxy->_EngineID)
{
    _ObjectTemplate = Persistent<ObjectTemplate>::New(_EngineProxy->Isolate(), ObjectTemplate::New());
    _ObjectTemplate->SetInternalFieldCount(2); // (one for the associated proxy, and one for the associated managed object ID)
}

ObjectTemplateProxy::ObjectTemplateProxy(V8EngineProxy* engineProxy, Local<ObjectTemplate> objectTemplate)
    :ProxyBase(ObjectTemplateProxyClass), _EngineProxy(engineProxy), _EngineID(engineProxy->_EngineID)
{
    _ObjectTemplate = Persistent<ObjectTemplate>::New(_EngineProxy->Isolate(), objectTemplate);
    _ObjectTemplate->SetInternalFieldCount(2); // (one for the associated proxy, and one for the associated managed object ID)
}

ObjectTemplateProxy::~ObjectTemplateProxy()
{
    if (!V8EngineProxy::IsDisposed(_EngineID))
    {
        BEGIN_ISOLATE_SCOPE(_EngineProxy);
        BEGIN_CONTEXT_SCOPE(_EngineProxy);

        if (!_ObjectTemplate.IsEmpty())
            _ObjectTemplate.Dispose();

        END_CONTEXT_SCOPE;
        END_ISOLATE_SCOPE;
    }

    _EngineProxy = nullptr;
}

// ------------------------------------------------------------------------------------------------------------------------

void ObjectTemplateProxy::RegisterNamedPropertyHandlers(
    ManagedNamedPropertyGetter getter, 
    ManagedNamedPropertySetter setter, 
    ManagedNamedPropertyQuery query, 
    ManagedNamedPropertyDeleter deleter, 
    ManagedNamedPropertyEnumerator enumerator)
{
    NamedPropertyGetter = getter; 
    NamedPropertySetter = setter; 
    NamedPropertyQuery = query; 
    NamedPropertyDeleter = deleter; 
    NamedPropertyEnumerator = enumerator;

    _ObjectTemplate->SetNamedPropertyHandler(GetProperty, SetProperty, GetPropertyAttributes, DeleteProperty, GetPropertyNames);
}

void ObjectTemplateProxy::RegisterIndexedPropertyHandlers(
    ManagedIndexedPropertyGetter getter, 
    ManagedIndexedPropertySetter setter, 
    ManagedIndexedPropertyQuery query, 
    ManagedIndexedPropertyDeleter deleter, 
    ManagedIndexedPropertyEnumerator enumerator)
{
    IndexedPropertyGetter = getter; 
    IndexedPropertySetter = setter; 
    IndexedPropertyQuery = query; 
    IndexedPropertyDeleter = deleter; 
    IndexedPropertyEnumerator = enumerator;

    _ObjectTemplate->SetIndexedPropertyHandler(GetProperty, SetProperty, GetPropertyAttributes, DeleteProperty, GetPropertyIndices);
}

// ------------------------------------------------------------------------------------------------------------------------

void ObjectTemplateProxy::UnregisterNamedPropertyHandlers()
{
    _ObjectTemplate->SetNamedPropertyHandler((NamedPropertyGetterCallback)nullptr);
}

void ObjectTemplateProxy::UnregisterIndexedPropertyHandlers()
{
    _ObjectTemplate->SetIndexedPropertyHandler((IndexedPropertyGetterCallback)nullptr);
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Value> ObjectTemplateProxy::GetProperty(Local<String> hName, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto str = proxy->_EngineProxy->GetNativeString(*hName); // TODO: This can be faster - no need to allocate every time!
                auto result = proxy->NamedPropertyGetter(str.String, maInfo); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();
                if (result != nullptr) return result->Handle(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Value>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Value> ObjectTemplateProxy::SetProperty(Local<String> hName, Local<Value> value, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto str = proxy->_EngineProxy->GetNativeString(*hName);
                HandleProxy *val = proxy->_EngineProxy->GetHandleProxy(value);
                auto result = proxy->NamedPropertySetter(str.String, val, maInfo); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();
                if (result != nullptr) return result->Handle(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Value>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Integer> ObjectTemplateProxy::GetPropertyAttributes(Local<String> hName, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto str = proxy->_EngineProxy->GetNativeString(*hName);
                int result = proxy->NamedPropertyQuery(str.String, maInfo); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();
                if (result >= 0)
                    return Handle<v8::Integer>(v8::Integer::New(result));
            }
        }
    }

    return Handle<Integer>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Boolean> ObjectTemplateProxy::DeleteProperty(Local<String> hName, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto str = proxy->_EngineProxy->GetNativeString(*hName);
                int result = proxy->NamedPropertyDeleter(str.String, maInfo); // (assumes the 'str' memory will be released by the managed side)
                str.Dispose();

                // if 'result' is < 0, then this represents an "undefined" return value, otherwise 0 == false, and > 0 is true.

                if (result >= 0)
                    return Handle<v8::Boolean>(v8::Boolean::New(result != 0 ? true : false));
            }
        }
    }

    return Handle<Boolean>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Array> ObjectTemplateProxy::GetPropertyNames(const AccessorInfo& info) // (Note: consider HasOwnProperty)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto result = proxy->NamedPropertyEnumerator(maInfo); // (assumes the 'str' memory will be released by the managed side)
                if (result != nullptr) return result->Handle().As<Array>(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Array>();
}

// . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . . .

Handle<Value> ObjectTemplateProxy::GetProperty(uint32_t index, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto result = proxy->IndexedPropertyGetter(index, maInfo); // (assumes the 'str' memory will be released by the managed side)
                if (result != nullptr) return result->Handle(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Value>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Value> ObjectTemplateProxy::SetProperty(uint32_t index, Local<Value> value, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                HandleProxy *val = proxy->_EngineProxy->GetHandleProxy(value);
                auto result = proxy->IndexedPropertySetter(index, val, maInfo); // (assumes the 'str' memory will be released by the managed side)
                if (result != nullptr) return result->Handle(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Value>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Integer> ObjectTemplateProxy::GetPropertyAttributes(uint32_t index, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                int result = proxy->IndexedPropertyQuery(index, maInfo); // (assumes the 'str' memory will be released by the managed side)
                if (result >= 0)
                    return Handle<v8::Integer>(v8::Integer::New(result));
            }
        }
    }

    return Handle<Integer>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Boolean> ObjectTemplateProxy::DeleteProperty(uint32_t index, const AccessorInfo& info)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                int result = proxy->IndexedPropertyDeleter(index, maInfo); // (assumes the 'str' memory will be released by the managed side)

                // if 'result' is < 0, then this represents an "undefined" return value, otherwise 0 == false, and > 0 is true.

                if (result >= 0)
                    return Handle<v8::Boolean>(v8::Boolean::New(result != 0 ? true : false));
            }
        }
    }

    return Handle<Boolean>();
}

// ------------------------------------------------------------------------------------------------------------------------

Handle<Array> ObjectTemplateProxy::GetPropertyIndices(const AccessorInfo& info) // (Note: consider HasOwnProperty)
{
    auto obj = info.Holder();

    if (obj->InternalFieldCount() > 1)
    {
        auto field = obj->GetInternalField(0);
        if (!field->IsUndefined())
        {
            auto proxy = reinterpret_cast<ObjectTemplateProxy*>(obj->GetAlignedPointerFromInternalField(0));

            if (proxy != nullptr && proxy->Type == ObjectTemplateProxyClass)
            {
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-fpermissive"
                auto managedObjectID = (int32_t)obj->GetInternalField(1).As<External>()->Value();
#pragma GCC diagnostic pop
                ManagedAccessorInfo maInfo(proxy, managedObjectID, info);
                auto result = proxy->IndexedPropertyEnumerator(maInfo); // (assumes the 'str' memory will be released by the managed side)
                if (result != nullptr) return result->Handle().As<Array>(); // (the result was create via p/invoke calls, but is expected to be tracked and freed on the managed side)
                // (result == null == undefined [which means the managed side didn't return anything])
            }
        }
    }

    return Handle<Array>();
}

// ------------------------------------------------------------------------------------------------------------------------

HandleProxy* ObjectTemplateProxy::CreateObject(int32_t managedObjectID)
{
    auto obj = _ObjectTemplate->NewInstance();
    auto proxyVal = _EngineProxy->GetHandleProxy(obj);
    ConnectObject(proxyVal, managedObjectID, this);
    return proxyVal;
}

// ------------------------------------------------------------------------------------------------------------------------
