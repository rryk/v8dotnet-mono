using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

#if V2 || V3 || V3_5
#else
using System.Dynamic;
#endif

namespace V8.Net
{
    // ========================================================================================================================

    /// <summary>
    /// Represents a JavaScript callback function for a managed class method.
    /// </summary>
    /// <param name="isConstructCall">True only if this function is being called to construct a new object (such as using the "new" operator within JavaScript).
    /// If this is true, the function is expected to create and return a new object (as the constructor for that object).</param>
    /// <param name="args">The arguments supplied for the JavaScript function call.</param>
    public delegate InternalHandle JSFunction(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args);

    // ========================================================================================================================

    public unsafe class FunctionTemplate : TemplateBase<IV8Function>
    {
        // --------------------------------------------------------------------------------------------------------------------

        internal Int32 _FunctionTemplateID; // (ID within IndexedObjectList)

        internal NativeFunctionTemplateProxy* _NativeFunctionTemplateProxy;

        public string ClassName { get; private set; }

        /// <summary>
        /// Set this to an object that implements a call-back to execute when the function associated with this FunctionTemplate is called within JavaScript.
        /// </summary>
        readonly Dictionary<Type, int> _FunctionsByType = new Dictionary<Type, int>();

        /// <summary>
        /// The V8 engine automatically creates two templates with every function template: one for object creation (instances) and one for function object itself (prototype inheritance).
        /// This property returns the ObjectTemplate wrapper associated with the V8 native instance template for creating new objects using the function in this template as the constructor.
        /// </summary>
        public ObjectTemplate InstanceTemplate { get; private set; }

        /// <summary>
        /// The V8 engine automatically creates two templates with every function template: one for object creation (instances) and one for object inheritance (prototypes).
        /// This property returns the ObjectTemplate wrapper associated with the prototype template for the function in this template.
        /// </summary>
        public ObjectTemplate PrototypeTemplate { get; private set; }

        // --------------------------------------------------------------------------------------------------------------------

        public FunctionTemplate()
        {
            if (Assembly.GetCallingAssembly() != Assembly.GetExecutingAssembly())
                throw new InvalidOperationException("You much create function templates by calling 'V8Engine.CreateFunctionTemplate()'.");
        }

        ~FunctionTemplate()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_NativeFunctionTemplateProxy != null)
            {
                V8NetProxy.DeleteFunctionTemplateProxy(_NativeFunctionTemplateProxy); // (delete the corresponding native object as well; WARNING: This is done on the GC thread!)
                _NativeFunctionTemplateProxy = null;
            }

            if (_FunctionTemplateID != -1)
                lock (_Engine._FunctionTemplates)
                {
                    _Engine._FunctionTemplates.Remove(_FunctionTemplateID);
                    _FunctionTemplateID = -1;
                }
        }

        internal void _Initialize(V8Engine v8EngineProxy, string className)
        {
            ClassName = className;

            _Initialize(v8EngineProxy,
                (NativeFunctionTemplateProxy*)V8NetProxy.CreateFunctionTemplateProxy(
                    v8EngineProxy._NativeV8EngineProxy,
                    ClassName,
                    _SetDelegate<ManagedJSFunctionCallback>(_CallBack)) // (create a corresponding native object)
            );
        }

        internal void _Initialize(V8Engine v8EngineProxy, NativeFunctionTemplateProxy* nativeFunctionTemplateProxy)
        {
            if (v8EngineProxy == null)
                throw new ArgumentNullException("v8EngineProxy");

            if (nativeFunctionTemplateProxy == null)
                throw new ArgumentNullException("nativeFunctionTemplateProxy");

            _Engine = v8EngineProxy;

            _FunctionTemplateID = _Engine._FunctionTemplates.Add(this);

            _NativeFunctionTemplateProxy = nativeFunctionTemplateProxy;

            InstanceTemplate = _Engine.CreateObjectTemplate<ObjectTemplate>();
            InstanceTemplate._Initialize(_Engine, (NativeObjectTemplateProxy*)V8NetProxy.GetFunctionInstanceTemplateProxy(_NativeFunctionTemplateProxy));

            PrototypeTemplate = _Engine.CreateObjectTemplate<ObjectTemplate>();
            PrototypeTemplate._Initialize(_Engine, (NativeObjectTemplateProxy*)V8NetProxy.GetFunctionPrototypeTemplateProxy(_NativeFunctionTemplateProxy));
        }

        // --------------------------------------------------------------------------------------------------------------------

        HandleProxy* _CallBack(Int32 managedObjectID, bool isConstructCall, HandleProxy* _this, HandleProxy** args, Int32 argCount)
        {
            // ... get a handle to the native "this" object ...

            using (InternalHandle hThis = new InternalHandle(_this, false))
            {

                //// ... get a managed wrapper for the new object (this also connects the native object to the specified template) ...
                //??var thisObj = isConstructCall ? _V8Engine._GetObject<V8ManagedObject>(InstanceTemplate, hThis, true, true)
                //    : _V8Engine._GetObject<V8NativeObject>(null, hThis, true, true);

                // ... wrap the arguments ...

                InternalHandle[] _args = new InternalHandle[argCount];
                int i;

                for (i = 0; i < argCount; i++)
                    _args[i].Set(args[i], false); // (since these will be disposed immediately after, the "first" flag is not required [this also prevents it from getting passed on])

                int funcID;
                V8Engine._ObjectInfo funcObjInfo;
                IV8Function func;
                InternalHandle result = null;

                try
                {
                    // ... call all function types (multiple custom derived function types are allowed, but only one of each type) ...

                    var callbackTypes = _FunctionsByType.Keys.ToArray();

                    for (i = callbackTypes.Length - 1; i >= 0; i--)
                    {
                        funcID = _FunctionsByType[callbackTypes[i]];
                        if (funcID >= 0)
                        {
                            funcObjInfo = _Engine._Objects[funcID];
                            func = funcObjInfo != null ? funcObjInfo.ManagedObject as IV8Function : null;
                            if (func != null && func.Callback != null)
                            {
                                result = func.Callback(_Engine, isConstructCall, hThis, _args);

                                if (result != null) break;
                            }
                            else _FunctionsByType.Remove(callbackTypes[i]); // (was GC'd, so remove it!)
                        }
                    }
                }
                finally
                {
                    for (i = 0; i < _args.Length; i++)
                        _args[i].Dispose();
                }

                if (isConstructCall && result.HasManagedObject && result.ManagedObject is IV8ManagedObject && result.ManagedObject.Handle == hThis)
                    throw new InvalidOperationException("You've attempted to return the type '" + result.ManagedObject.GetType().Name + "' which implements IV8ManagedObject in a construction call (using 'new' in JavaScript) to wrap the new native object. The native V8 engine only supports interceptor hooks for objects generated from object templates.  You will need to first derive/implement from V8NativeObject/IV8NativeObject for construction calls, then wrap it around your object (or rewrite your object to use V8NativeObject directly instead and use the 'SetAccessor()' method).");

                return result;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the specified V8Function object type associated with this function template.
        /// There can only ever be one native V8 function object per native V8 function template in a single native V8 JavaScript context;
        /// however, V8.NET (the managed side) does allow one function type per template. In this case, a single call triggers all derived types at once.
        /// The first callback to return a value terminates the cycle and any following callbacks are ignored.
        /// <para>WARNING: The returned function object will be garbage collected if you don't store the reference anywhere. If this happens, then calling 
        /// the function object in JavaScript will return "undefined".</para>
        /// </summary>
        /// <typeparam name="T">A type that implements IV8Function, or derives from V8Function.</typeparam>
        /// <param name="callback">When a new instance of type 'T' is created, it's 'Callback' property will overwritten by this value (replacing anything that may be set when it was created).
        /// It is expect to provide a callback method when using the default 'V8Function' object, but if you have a custom derivation you can set this to 'null'.</param>
        public T GetFunctionObject<T>(JSFunction callback = null) where T : class, IV8Function, new()
        {
            int funcID;
            V8Engine._ObjectInfo funcObjInfo;
            IV8Function func;

            if (_FunctionsByType.TryGetValue(typeof(T), out funcID))
            {
                funcObjInfo = _Engine._Objects[funcID];
                func = funcObjInfo != null ? funcObjInfo.ManagedObject as IV8Function : null;
                if (func != null)
                    return (T)func;
            }

            // ... get the v8 "Function" object ...

            InternalHandle hNativeFunc = V8NetProxy.GetFunction(_NativeFunctionTemplateProxy);

            // ... create a managed wrapper for the V8 "Function" object (note: functions inherit the native V8 "Object" type) ...

            func = _Engine.GetObject<T>(hNativeFunc.PassOn(), true, false); // (note: this will "connect" the native object [hNativeFunc] to a new managed V8Function wrapper, and set the prototype!)

            if (!(func is IV8Function)) // (debug check)
                throw new InvalidOperationException("Conflict error: An existing IV8NativeObject object already exists for the function handle.");

            if (callback != null)
                func.Callback = callback;

            funcObjInfo = _Engine._Objects[func.ID];

            funcObjInfo._Template = this;

            // ... get the function's prototype object, wrap it, and give it to the new function object ...
            // (note: this is a special case, because the function object auto generates the prototype object natively using an existing object template)

            InternalHandle hProto = V8NetProxy.GetObjectPrototype(funcObjInfo._Handle); // TODO: Is it necessary to do this since reading the proxy property usually gets it for us IF NEEDED?
            var managedProtoObj = _Engine._GetObject<V8ManagedObject>(PrototypeTemplate, hProto.PassOn(), true, false); // (this is in case the object already exists)
            var protoObjInfo = _Engine._Objects[managedProtoObj.ID];

            _FunctionsByType[typeof(T)] = func.ID; // (this exists to index functions by type)

            protoObjInfo.Initialize(); // (initialize the prototype first)
            funcObjInfo.Initialize();

            return (T)func;
        }

        /// <summary>
        /// Returns a JavaScript V8Function object instance associated with this function template.
        /// There can only ever be ONE V8 function object per V8 function template in a single V8 JavaScript context;
        /// however, V8.NET does allow one MANAGED function type per managed template. In this case, a single call triggers all derived types at once.
        /// The first callback to return a value terminates the cycle and any following callbacks are ignored.
        /// <para>WARNING: The returned function object will be garbage collected if you don't store the reference anywhere. If this happens, then calling 
        /// the function object in JavaScript will return "undefined". This is because function object callbacks are dynamic and are only valid when
        /// the calling object is still in use.</para>
        /// </summary>
        /// <param name="callback">When a new instance of V8Function is created, it's 'Callback' property will set to the specified value.
        /// If you don't provide a callback, then calling the function in JavaScript will simply do nothing and return "undefined".</param>
        public IV8Function GetFunctionObject(JSFunction callback) { return GetFunctionObject<V8Function>(callback); }

        // --------------------------------------------------------------------------------------------------------------------

        public IV8ManagedObject CreateInstance(params InternalHandle[] args) // TODO: Parameter passing needs testing.
        {
            HandleProxy** _args = null;

            if (args.Length > 0)
            {
                _args = (HandleProxy**)Utilities.AllocPointerArray(args.Length);
                for (var i = 0; i < args.Length; i++)
                    _args[i] = args[i];
            }

            // (note: the special case here is that the native function object will use its own template to create instances)

            var objInfo = _Engine._CreateManagedObject<V8ManagedObject>(this, null);
            objInfo._Template = InstanceTemplate;

            try
            {
                objInfo._Handle.Set(V8NetProxy.CreateFunctionInstance(_NativeFunctionTemplateProxy, objInfo._ID, args.Length, _args));
                // (note: setting '_NativeObject' also updates it's '_ManagedObject' field if necessary.
            }
            catch (Exception ex)
            {
                // ... something went wrong, so remove the new managed object ...
                _Engine._Objects.Remove(objInfo._ID);
                throw ex;
            }
            finally
            {
                Utilities.FreeNativeMemory((IntPtr)_args);
            }

            objInfo.Initialize();

            return (IV8ManagedObject)objInfo.ManagedObject;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// This is called when the managed function object info entry is ready to be deleted.
        /// </summary>
        internal void _RemoveFunctionType(int id)
        {
            var callbackTypes = _FunctionsByType.Keys.ToArray();
            for (var i = 0; i < callbackTypes.Length; i++)
                if (_FunctionsByType[callbackTypes[i]] == id)
                    _FunctionsByType[callbackTypes[i]] = -1;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
