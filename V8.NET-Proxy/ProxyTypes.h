// V8 proxy exports header for the Dream Space internet development framework.
// This source is released under LGPL.

#include <exception>
#include <vector>
#if (_MSC_PLATFORM_TOOLSET >= 110)
#include <mutex>
#endif
#include <v8stdint.h>
#include "Platform.h"

using namespace std;

#if (_MSC_PLATFORM_TOOLSET < 110)
#define nullptr NULL
#endif

#if _WIN32 || _WIN64
#include <windows.h>
#include <oleauto.h>
#define ALLOC_MANAGED_MEM(size) GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT, size)
#define REALLOC_MANAGED_MEM(ptr, size) GlobalReAlloc(ptr, size, GMEM_MOVEABLE)
#define FREE_MANAGED_MEM(ptr) { GlobalFree(ptr); ptr = nullptr; }
//??#define ALLOC_MANAGED_STRING(size) CoTaskMemAlloc(GMEM_FIXED|GMEM_ZEROINIT, size)
#define FREE_MARSHALLED_STRING(ptr) { CoTaskMemFree(ptr); ptr = nullptr; }
#define STDCALL __stdcall
#else
#include <glib.h>
#define ALLOC_MANAGED_MEM(size) g_malloc(size)
#define FREE_MANAGED_MEM(ptr) g_free(ptr)
#define STDCALL __stdcall
#endif

#define USING_V8_SHARED 1
#define V8_USE_UNSAFE_HANDLES 1 // (see https://groups.google.com/forum/#!topic/v8-users/oBE_DTpRC08)

#include <v8.h>
#include <v8-debug.h>

using namespace v8;

#define EXPORT __declspec(dllexport)

// ========================================================================================================================

#define BEGIN_HANDLE_SCOPE(_this) \
{ \
    v8::Locker __lockScope(_this->_Isolate); \
    v8::HandleScope __handleScope;

#define END_HANDLE_SCOPE \
    __handleScope; /* (prevent non-usage warnings) */ \
    __lockScope; \
}

#define BEGIN_ISOLATE_SCOPE(_this) \
{ \
    v8::Locker __lockScope(_this->Isolate()); \
    v8::Isolate::Scope __isolateScope(_this->Isolate()); \
    v8::HandleScope __handleScope;

#define END_ISOLATE_SCOPE \
    __handleScope; \
    __isolateScope; \
    __lockScope; \
}

#define BEGIN_CONTEXT_SCOPE(_this) \
{ \
    v8::Context::Scope __contextScope(_this->Context());

#define END_CONTEXT_SCOPE \
    __contextScope; \
}

// ========================================================================================================================

class ProxyBase;
class ObjectTemplateProxy;
class FunctionTemplateProxy;
class V8EngineProxy;

struct HandleProxy;
struct HandleValue;

// Get rid of some linker warnings regarding certain V8 object references.
// (see https://groups.google.com/forum/?fromgroups=#!topic/v8-users/OuZPd0n-oRg)
namespace v8 { namespace internal {
    class Object { };
    class Isolate { };
}}

// ========================================================================================================================
// Utility functions.

///**
//* Converts a given V8 string into a uint16_t* string using ALLOC_MANAGED_MEM().
//* The string is expected to be freed by calling FREE_MANAGED_MEM(), or within a managed assembly.
//*/
//??uint16_t* V8StringToUInt16(v8::String* str);

// ========================================================================================================================

//??#pragma enum(4)
// Types supported by HandleProxy.
enum JSValueType: int32_t
{
    JSV_ExecutionError = -3, // An error has occurred while attempting to execute the compiled script.
    JSV_CompilerError = -2, // An error has occurred compiling the script (usually a syntax error).
    JSV_InternalError = -1, // An internal error has occurred (before or after script execution).
    JSV_Undefined = 0, // Value is unknown or not set.
    JSV_Null,
    JSV_Bool, // The value is a Boolean, as supported within JavaScript for true/false conditions.
    JSV_BoolObject, // The value is a Boolean object (object reference), as supported within JavaScript when executing "new Boolean()".
    JSV_Int32, // The value is a 32-bit integer, as supported within JavaScript for bit operations.
    JSV_Number, // The value is a JavaScript 64-bit number.
    JSV_NumberObject, // The value is a JavaScript 64-bit number object (object reference), as supported within JavaScript when executing "new Number()".
    JSV_String, // The value is a UTF16 string.
    JSV_StringObject, // The value is a JavaScript string object (object reference), as supported within JavaScript when executing "new String()".
    JSV_Object, // The value is a non-value (object reference).
    JSV_Function, // The value is a reference to a JavaScript function (object reference).
    JSV_Date, // The date value is the number of milliseconds since epoch [1970-01-01 00:00:00 UTC+00] (a double value stored in 'Number').
    JSV_Array, // The value proxy represents a JavaScript array of various values.
    JSV_RegExp, // The value is a reference to a JavaScript RegEx object (object reference).

    // (when updating, don't forget to update V8EngineProxy.Enums.cs also!)
};
//??#pragma enum(pop)

// ========================================================================================================================

#pragma pack(push, 1)
// While "HandleProxy" tracks values/objects by handle, this type helps to marshal the underlying values to the managed side when needed.
struct HandleValue
{
    union
    {
        bool V8Boolean; // JavaScript Boolean.
        int64_t V8Integer; // JavaScript 32-bit integer, but this is 64-bit to maintain consistent union size!!! (double is 64-bit in x64, and 32-bit in x86)
        double V8Number; // JavaScript number (double [32/64-bit float]).
    };

    union
    {
        uint16_t *V8String; // JavaScript string.
        int64_t _V8String; // (to keep pointer sizes consistent between 32 and 64 bit systems)
    };

    HandleValue();

    ~HandleValue();

    void Dispose();
};
#pragma pack(pop)

// ========================================================================================================================

#pragma pack(push, 1)
// Provides a mechanism by which to keep track of V8 objects associated with managed side objects.
struct HandleProxy
{
private:
    int32_t _ID; // The ID of this handle (handles are cached/recycled and not destroyed). The ID is also used on the managed side.

    int32_t _ObjectID; // The ID (index) of any associated managed object in V8.NET.  This is -1 by default, and is only update when 'GetManagedObjectID()' is called.

    JSValueType _Type; // Note: a 32-bit type value (the managed code will expect 4 bytes).

    HandleValue _Value; // The value is only valid when 'UpdateValue()' is called. Note: sizeof(double) + sizeof(uint16_t*)

    int64_t _ManagedReferenceCount; // The number of references on the managed side.

    int32_t _Disposed; // (0: handle is active, 1: managed disposing in progress, 3: VIRTUALLY disposed [cached on native side for reuse])

    int32_t _EngineID;

    union
    {
        V8EngineProxy* _EngineProxy;
        int64_t __EngineProxy; // (to keep pointer sizes consistent between 32 and 64 bit systems)
    };

    Persistent<Value> _Handle; // Reference to a JavaScript object (persisted handle for future reference - WARNING: Must be explicitly released when no longer needed!).

    static void _DisposeCallback(Isolate* isolate, Persistent<Value> object, void* parameter);
    static void _RevivableCallback(Isolate* isolate, Persistent<Value>* object, HandleProxy* parameter);

protected:

    HandleProxy(V8EngineProxy* engineProxy, int id);
    ~HandleProxy();

    HandleProxy* Initialize(v8::Handle<Value> handle);
    HandleProxy* SetHandle(v8::Handle<Value> handle);
    HandleProxy* SetDate(double ms);

    bool _Dispose(bool registerDisposal);

public:

    int32_t SetManagedObjectID(int32_t id) { return (_ObjectID = id); }

    bool IsError() { return _Type < 0; }

    // Disposes of the handle that is wrapped by this proxy instance.
    bool Dispose();

    // (expected to be called by a managed garbage collection thread [of some sort, but not the main thread])
    void _ManagedGCCallback();

    // Attempts to delete the handle, which will succeed only if the engine is gone, otherwise Dispose() is called)
    void Delete();

    V8EngineProxy* EngineProxy() { return _EngineProxy; }
    int32_t EngineID() { return _EngineID; }

    Handle<Value> Handle();

    void MakeWeak();
    void MakeStrong();

    int GetManagedObjectID();
    void UpdateValue();

    friend V8EngineProxy;
    friend ObjectTemplateProxy;
    friend FunctionTemplateProxy;
};
#pragma pack(pop)

// ========================================================================================================================

/**
* Usually allocated on the stack before being passed to a managed call-back when triggered by script access.
*/
#pragma pack(push, 1)
struct ManagedAccessorInfo
{
private:

    ObjectTemplateProxy* _ObjectProxy; // (this is AccessorInfo::Holder() related, or nullptr for non-ObjectTemplate created objects) 
    int32_t _ObjectID; // If set (>=0), then this instance is a new JavaScript object, created from a template, and associated with a managed object. Default is -1.
    //?? (not sure if the ID is needed here)

public:

    Local<Value> Data;
    Local<Object> This;

    ManagedAccessorInfo(ObjectTemplateProxy* objectProxy, int32_t managedObjectID, const AccessorInfo& info)
        : _ObjectProxy(objectProxy), _ObjectID(managedObjectID)
    {
        Data = info.Data();
        This = info.This();
    }
};
#pragma pack(pop)

// ========================================================================================================================
// Managed call-back delegate types.

typedef void (STDCALL *CallbackAction)();

/**
* NamedProperty[Getter|Setter] are used as interceptors on object.
* See ObjectTemplate::SetNamedPropertyHandler.
*/
typedef HandleProxy* (STDCALL *ManagedNamedPropertyGetter)(uint16_t* propertyName, const ManagedAccessorInfo& info);

/**
* Returns the value if the setter intercepts the request.
* Otherwise, returns an empty handle.
*/
typedef HandleProxy* (STDCALL *ManagedNamedPropertySetter)(uint16_t* propertyName, HandleProxy* value, const ManagedAccessorInfo& info);

/**
* Returns a non-empty value (>=0) if the interceptor intercepts the request.
* The result is an integer encoding property attributes (like v8::None,
* v8::DontEnum, etc.)
*/
typedef PropertyAttribute (STDCALL *ManagedNamedPropertyQuery)(uint16_t* propertyName, const ManagedAccessorInfo& info);

/**
* Returns a value indicating if the deleter intercepts the request.
* The return value is true (>0) if the property could be deleted and false (0)
* otherwise.
*/
typedef int (STDCALL *ManagedNamedPropertyDeleter)(uint16_t* propertyName, const ManagedAccessorInfo& info);

/**
* Returns an array containing the names of the properties the named
* property getter intercepts.
*/
typedef HandleProxy* (STDCALL *ManagedNamedPropertyEnumerator)(const ManagedAccessorInfo& info);

// ------------------------------------------------------------------------------------------------------------------------
/**
* Returns the value of the property if the getter intercepts __stdcall
*/
typedef HandleProxy* (STDCALL *ManagedIndexedPropertyGetter)(uint32_t index, const ManagedAccessorInfo& info);

/**
* Returns the value if the setter intercepts the request.
* Otherwise, returns an empty handle.
*/
typedef HandleProxy* (STDCALL *ManagedIndexedPropertySetter)(uint32_t index, HandleProxy* value, const ManagedAccessorInfo& info);

/**
* Returns a non-empty handle if the interceptor intercepts the request.
* The result is an integer encoding property attributes.
*/
typedef PropertyAttribute (STDCALL *ManagedIndexedPropertyQuery)(uint32_t index, const ManagedAccessorInfo& info);

/**
* Returns a non-empty handle if the deleter intercepts the request.
* The return value is true if the property could be deleted and false
* otherwise.
*/
typedef int (STDCALL *ManagedIndexedPropertyDeleter)(uint32_t index, const ManagedAccessorInfo& info);

/**
* Returns an array containing the indices of the properties the
* indexed property getter intercepts.
*/
typedef HandleProxy* (STDCALL *ManagedIndexedPropertyEnumerator)(const ManagedAccessorInfo& info);

// ------------------------------------------------------------------------------------------------------------------------

/**
* Intercepts requests on objects with getters applied.
*/
typedef HandleProxy* (STDCALL *ManagedAccessorGetter)(HandleProxy *_this, uint16_t* propertyName);

/**
* Intercepts requests on objects with setters applied.
The return is always undefined, unless an error occurs.
*/
typedef HandleProxy* (STDCALL *ManagedAccessorSetter)(HandleProxy *_this, uint16_t* propertyName, HandleProxy* value);

// ------------------------------------------------------------------------------------------------------------------------

// A managed call-back that is triggered when a native object has no more references.
// When the managed side is notified of no more JavaScript/V8 references, then the associated strong-reference on the managed side is cleared to allow the
// managed weak reference to track the managed object.  Persisted object handles will be disposed when the managed objects are finalized.
typedef bool (STDCALL *ManagedV8GarbageCollectionRequestCallback)(HandleProxy *hProxy);

// ========================================================================================================================
// Proxy object type enums.

enum  TProxyObjectType
{
    ObjectTemplateProxyClass,
    FunctionTemplateProxyClass,
    V8EngineProxyClass
};

// ========================================================================================================================
// The proxy base class helps to identify objects when references are passed between native and managed mode.

#pragma pack(push, 1)
class ProxyBase
{
protected:
    TProxyObjectType Type;

    ProxyBase(TProxyObjectType type):Type(type) { }
};
#pragma pack(pop)

// ========================================================================================================================

/**
* A proxy class to encapsulate the call-back methods needed to resolve properties for representing a managed object.
*/
#pragma pack(push, 1)
class ObjectTemplateProxy: ProxyBase
{
protected:

    V8EngineProxy* _EngineProxy;
    int32_t _EngineID;
    int32_t _ObjectID; // ObjectTemplate will have a "shared" object ID for use with associating accessors.
    Persistent<ObjectTemplate> _ObjectTemplate;

    ManagedNamedPropertyGetter NamedPropertyGetter;
    ManagedNamedPropertySetter NamedPropertySetter;
    ManagedNamedPropertyQuery NamedPropertyQuery;
    ManagedNamedPropertyDeleter NamedPropertyDeleter;
    ManagedNamedPropertyEnumerator NamedPropertyEnumerator;

    ManagedIndexedPropertyGetter IndexedPropertyGetter;
    ManagedIndexedPropertySetter IndexedPropertySetter;
    ManagedIndexedPropertyQuery IndexedPropertyQuery;
    ManagedIndexedPropertyDeleter IndexedPropertyDeleter;
    ManagedIndexedPropertyEnumerator IndexedPropertyEnumerator;

public:

    // Called when created by V8EngineProxy.
    ObjectTemplateProxy(V8EngineProxy* engineProxy);

    // Called by FunctionTemplateProxy to create a wrapper for the existing templates (auto generated with the FunctionTemplate instance).
    ObjectTemplateProxy(V8EngineProxy* engineProxy, Local<ObjectTemplate> objectTemplate);

    ~ObjectTemplateProxy();

    void RegisterNamedPropertyHandlers(
        ManagedNamedPropertyGetter getter, 
        ManagedNamedPropertySetter setter, 
        ManagedNamedPropertyQuery query, 
        ManagedNamedPropertyDeleter deleter, 
        ManagedNamedPropertyEnumerator enumerator);

    void RegisterIndexedPropertyHandlers(
        ManagedIndexedPropertyGetter getter, 
        ManagedIndexedPropertySetter setter, 
        ManagedIndexedPropertyQuery query, 
        ManagedIndexedPropertyDeleter deleter, 
        ManagedIndexedPropertyEnumerator enumerator);

    void UnregisterNamedPropertyHandlers();
    void UnregisterIndexedPropertyHandlers();

    static Handle<Value> GetProperty(Local<String> hName, const AccessorInfo& info);
    static Handle<Value> SetProperty(Local<String> hName, Local<Value> value, const AccessorInfo& info);
    static Handle<Integer> GetPropertyAttributes(Local<String> hName, const AccessorInfo& info);
    static Handle<Boolean> DeleteProperty(Local<String> hName, const AccessorInfo& info);
    static Handle<Array> GetPropertyNames(const AccessorInfo& info);

    static Handle<Value> GetProperty(uint32_t index, const AccessorInfo& info);
    static Handle<Value> SetProperty(uint32_t index, Local<Value> hValue, const AccessorInfo& info);
    static Handle<Integer> GetPropertyAttributes(uint32_t index, const AccessorInfo& info);
    static Handle<Boolean> DeleteProperty(uint32_t index, const AccessorInfo& info);
    static Handle<Array> GetPropertyIndices(const AccessorInfo& info);

    static void AccessorGetterCallbackProxy(Local<String> property, const PropertyCallbackInfo<Value>& info);
    static void AccessorSetterCallbackProxy(Local<String> property, Local<Value> value, const PropertyCallbackInfo<void>& info);

    HandleProxy* CreateObject(int32_t managedObjectID);

    void SetAccessor(int32_t managedObjectID, const uint16_t *name,
        ManagedAccessorGetter getter, ManagedAccessorSetter setter,
        v8::AccessControl access, v8::PropertyAttribute attributes);

    void Set(const uint16_t *name, HandleProxy *value, v8::PropertyAttribute attributes);

    friend V8EngineProxy;
    friend FunctionTemplateProxy;
};
#pragma pack(pop)

// ========================================================================================================================

typedef HandleProxy* (STDCALL *ManagedJSFunctionCallback)(int32_t managedObjectID, bool isConstructCall, HandleProxy *_this, HandleProxy** args, uint32_t argCount);

/**
* A proxy class to encapsulate the call-back methods needed to resolve properties for representing a managed object.
*/
#pragma pack(push, 1)
class FunctionTemplateProxy: ProxyBase
{
protected:

    V8EngineProxy* _EngineProxy;
    int32_t _EngineID;
    Persistent<FunctionTemplate> _FunctionTemplate;
    ObjectTemplateProxy* _InstanceTemplate;
    ObjectTemplateProxy* _PrototypeTemplate;

    ManagedJSFunctionCallback _ManagedCallback;

public:

    FunctionTemplateProxy(V8EngineProxy* engineProxy, uint16_t* className, ManagedJSFunctionCallback managedCallback = nullptr);
    ~FunctionTemplateProxy();

    void SetManagedCallback(ManagedJSFunctionCallback managedCallback);

    static Handle<Value> InvocationCallbackProxy(const Arguments& args);

    ObjectTemplateProxy* GetInstanceTemplateProxy();
    ObjectTemplateProxy* GetPrototypeTemplateProxy();

    HandleProxy* GetFunction();
    //??HandleProxy* GetPrototype(int32_t managedObjectID);

    HandleProxy* CreateInstance(int32_t managedObjectID, int32_t argCount, HandleProxy** args);

    void Set(const uint16_t *name, HandleProxy *value, v8::PropertyAttribute attributes);

    friend V8EngineProxy;
};
#pragma pack(pop)

// ========================================================================================================================

typedef void DebugMessageDispatcher();

// ========================================================================================================================

struct _StringItem
{
    V8EngineProxy *Engine;
    uint16_t* String;
    size_t Length;

    _StringItem();
    _StringItem(V8EngineProxy *engine, size_t length);
    _StringItem(V8EngineProxy *engine, v8::String* str);

    void Free(); // Releases the string memory.

    _StringItem ResizeIfNeeded(size_t newLength); // Resizes the string to support the specified new length.
    void Dispose(); // Disposes of the string if one exists.
    void Clear(); // Clears the fields without disposing anything.
};

// ========================================================================================================================

class V8EngineProxy: ProxyBase
{
protected:

    int32_t _EngineID; // NOTE: This MUST be the FIRST field (expected by the managed site).

    // Keeps track of engines being disposed to prevent managed handles from trying to dispose of invalidated V8 handles!
    // Why? If not, then we need to keep track of handles in some form of collection, list, or array. Since handles are a core part of values being handed
    // around, this would greatly impact performance. Since it is assumed that engines will not be created and disposed in large numbers, if at all, a
    // record of disposed engines is kept so handles can quickly check if they are ok to be disposed (note: managed handles are disposed on a GC thread!).
    static vector<bool> _DisposedEngines;
    static int32_t _NextEngineID;

    int32_t _NextNonTemplateObjectID;

    Isolate* _Isolate;
    ObjectTemplateProxy* _GlobalObjectTemplateProxy; // (for working with the managed side regarding the global scope)
    Persistent<Context> _Context;
    Persistent<Object> _GlobalObject; // (taken from the context)
    ManagedV8GarbageCollectionRequestCallback _ManagedV8GarbageCollectionRequestCallback;

    vector<_StringItem> _Strings; // An array (cache) of string buffers to reuse when marshalling strings.

    vector<HandleProxy*> _Handles; // An array of all allocated handles for this engine proxy.
    vector<int> _DisposedHandles; // An array of handles (by ID [index]) that have been disposed. The managed GC thread uses this, so beware!
    recursive_mutex _HandleSystemMutex; // A mutex is used to prevent access to the handle system as a "critical section".  NO ACCESS TO THE V8 ENGINE IS ALLOWED FOR MANAGED GARBAGE COLLECTION IN THIS CRITICAL SECTION.

public:

    Isolate* Isolate();
    Handle<Context> Context();

    V8EngineProxy(bool enableDebugging, DebugMessageDispatcher* debugMessageDispatcher, int debugPort);
    ~V8EngineProxy();

    // Returns the next object ID for objects that do NOT have a corresponding object.  These objects still need an ID, and are given values less than -1.
    int32_t GetNextNonTemplateObjectID() { return _NextNonTemplateObjectID--; }

    // Gets or allocates a string buffer from within the cached strings array.
    _StringItem GetNativeString(v8::String* str);

    // Disposes a string returned via 'GetNativeString()'.
    void DisposeNativeString(_StringItem &item);

    // Gets an available handle proxy, or creates a new one, for the specified handle.
    HandleProxy* GetHandleProxy(Handle<Value> handle);

    // Registers the handle proxy as disposed for recycling.
    void DisposeHandleProxy(HandleProxy *handleProxy);

    // Registers a request to dispose a handle proxy for recycling.
    // WARNING: This is expected to be called by the GC to flag handles for disposal.
    //??void RequestDisposeHandleProxy(HandleProxy *handleProxy);

    void  RegisterGCCallback(ManagedV8GarbageCollectionRequestCallback managedV8GarbageCollectionRequestCallback);

    static bool IsDisposed(int32_t engineID);

    void WithIsolateScope(CallbackAction action);
    void WithContextScope(CallbackAction action);
    void WithHandleScope(CallbackAction action);

    ObjectTemplateProxy* CreateObjectTemplate();
    HandleProxy* SetGlobalObjectTemplate(ObjectTemplateProxy* proxy);

    FunctionTemplateProxy* CreateFunctionTemplate(uint16_t *className, ManagedJSFunctionCallback callback);

    HandleProxy* Execute(uint16_t* script, uint16_t* sourceName);

    HandleProxy* CreateNumber(double num);
    HandleProxy* CreateInteger(int32_t num);
    HandleProxy* CreateBoolean(bool b);
    HandleProxy* CreateString(uint16_t* str);
    HandleProxy* CreateError(uint16_t* message, JSValueType errorType);
    HandleProxy* CreateDate(double ms);
    HandleProxy* CreateArray(HandleProxy** items, uint16_t length);
    HandleProxy* CreateArray(uint16_t** items, uint16_t length);
    HandleProxy* CreateObject(int32_t managedObjectID);
    HandleProxy* CreateNullValue();

    friend HandleProxy;
    friend ObjectTemplateProxy;
    friend FunctionTemplateProxy;
};

// ========================================================================================================================

extern "C"
{
    EXPORT void STDCALL ConnectObject(HandleProxy *handleProxy, int32_t managedObjectID, void* templateProxy);
}
