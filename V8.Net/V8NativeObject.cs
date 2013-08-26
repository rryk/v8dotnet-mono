﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if !(V1_1 || V2 || V3 || V3_5)
using System.Dynamic;
using System.Linq.Expressions;
#endif

namespace V8.Net
{
    // ========================================================================================================================

    /// <summary>
    /// An interface for the V8NativeObject object.
    /// </summary>
    public interface IV8NativeObject : IV8Object, IDisposable, IDynamicMetaObjectProvider
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// <para>Note: Override "Dispose()" instead of implementing destructors (finalizers) if required.</para>
        /// </summary>
        void Initialize(V8NativeObject owner);

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called when there are no more references and the object is ready to be disposed.
        /// You should always override this if you need to dispose of any native resources yourself.
        /// DO NOT rely on the destructor (finalizer).
        /// <para>Note: This can be called from the finalizer thread (indirectly through V8.NET), in which case
        /// 'GC.SuppressFinalize()' will be called automatically upon return (to prevent collection until the native side is also ready).</para>
        /// If overriding, make sure to call back to this base method.
        /// </summary>
        new void Dispose();

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// Represents a basic JavaScript object. This class wraps V8 functionality for operations required on any native V8 object (including managed ones).
    /// <para>This class implements 'DynamicObject' to make setting properties a bit easier.</para>
    /// </summary>
    public unsafe class V8NativeObject : IHandleBased, IV8NativeObject, IDynamicMetaObjectProvider
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the V8Engine instance that owns this object.
        /// The default implementation for 'V8NativeObject' is to cache and return 'base.Engine', since it inherits from 'Handle'.
        /// </summary>
        public V8Engine Engine { get { return _Engine ?? (_Engine = _Handle.Engine); } }
        internal V8Engine _Engine;

        public Handle AsHandle() { return _Handle; }
        public InternalHandle AsInternalHandle { get { return _Handle._Handle; } }
        public V8NativeObject Object { get { return this; } }

        /// <summary>
        /// The V8.NET ObjectTemplate or FunctionTemplate instance associated with this object, if any, or null if this object was not created using a V8.NET template.
        /// </summary>
        public ITemplate Template
        {
            get { return _Template; }
            internal set
            {
                if (_Template != null) ((ITemplateInternal)_Template)._ReferenceCount--;
                _Template = value;
                if (_Template != null) ((ITemplateInternal)_Template)._ReferenceCount++;
            }
        }
        ITemplate _Template;

        /// <summary>
        /// The V8.NET managed object ID used to track this object instance on both the native and managed sides.
        /// </summary>
        public Int32 ID
        {
            get { var id = _Handle.ObjectID; return id < 0 ? _ID ?? id : id; } // (this attempts to return the underlying managed object ID of the handle proxy, or the local ID if -1)
            internal set { _Handle.ObjectID = (_ID = value).Value; } // (once set, the managed object will be fixed to the ID as long as the underlying handle has a managed object ID of -1)
        }
        Int32? _ID;

        /// <summary>
        /// Another object of the same interface to direct actions to (such as 'Initialize()').
        /// If the generic type 'V8NativeObject&lt;T>' is used, then this is set to an instance of "T", otherwise this is set to "this" instance.
        /// </summary>
        public IV8NativeObject Proxy { get { return _Proxy; } }
        internal IV8NativeObject _Proxy;

        /// <summary>
        /// True if this object was initialized and is ready for use.
        /// </summary>
        public bool IsInitilized { get; internal set; }

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object.
        /// The default implementation for 'V8NativeObject' is to return itself, since it inherits from 'Handle'.
        /// Setting this property will call the inherited 'Set()' method to replace the handle associated with this object instance (this should never be done on
        /// objects created from templates ('V8ManagedObject' objects), otherwise callbacks from JavaScript to the managed side will not act as expected, if at all).
        /// </summary>
        public ObjectHandle Handle { get { return _Handle; } set { _Handle.Set((Handle)value); } }
        internal ObjectHandle _Handle = ObjectHandle.Empty;

#if !(V1_1 || V2 || V3 || V3_5)
        /// <summary>
        /// Returns a "dynamic" reference to this object (which is simply the handle instance, which has dynamic support).
        /// </summary>
        public virtual dynamic AsDynamic { get { return _Handle; } }
#endif

        /// <summary>
        /// The prototype of the object (every JavaScript object implicitly has a prototype).
        /// </summary>
        public ObjectHandle Prototype
        {
            get
            {
                if (_Prototype == null && _Handle.IsObjectType)
                {
                    // ... the prototype is not yet set, so get the prototype and wrap it ...
                    _Prototype = _Handle.Prototype;
                }

                return _Prototype;
            }
        }
        internal ObjectHandle _Prototype;

        /// <summary>
        /// Returns true if this object is ready to be garbage collected by the native side.
        /// </summary>
        public bool IsManagedObjectWeak { get { lock (Engine._Objects) { return _ID != null ? Engine._Objects[_ID.Value].IsGCReady : true; } } }

        /// <summary>
        /// Used internally to quickly determine when an instance represents a binder object type, or static type binder function (faster than reflection!).
        /// </summary>
        public BindingMode BindingType { get { return _BindingMode; } }
        internal BindingMode _BindingMode;

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle this[string propertyName]
        {
            get
            {
                return _Handle.GetProperty(propertyName);
            }
            set
            {
                _Handle.SetProperty(propertyName, value);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        public V8NativeObject()
        {
            _Proxy = this;
        }

        public V8NativeObject(IV8NativeObject proxy)
        {
            _Proxy = proxy ?? this;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// Derived objects should override this for construction instead of using the constructor.
        /// In the constructor, the object only exists as an empty shell.
        /// It's ok to setup non-v8 values in constructors, but be careful not to trigger any calls into the V8Engine itself.
        /// <para>Note: Because this method is virtual, it does not guarantee that 'IsInitialized' will be considered.  Implementations should check against
        /// the 'IsInitilized' property.</para>
        /// </summary>
        public virtual void Initialize()
        {
            if (_Proxy != this && !IsInitilized)
                _Proxy.Initialize(this);

            IsInitilized = true;
        }

        /// <summary>
        /// (Exists only to support the 'IV8NativeInterface' interface and should not be called directly - call 'Initialize()' instead.)
        /// </summary>
        public void Initialize(V8NativeObject owner)
        {
            if (!IsInitilized)
                Initialize();
        }

        /// <summary>
        /// This is called on the GC finalizer thread to flag that this managed object entry can be collected.
        /// <para>Note: There are no longer any managed references to the object at this point; HOWEVER, there may still be NATIVE ones.
        /// This means the object may survive this process, at which point it's up to the worker thread to clean it up when the native V8 GC is ready.</para>
        /// </summary>
        ~V8NativeObject()
        {
            if (_ID != null)
                lock (Engine._Objects)
                {
                    Engine._Objects[_ID.Value].DoFinalize(this); // (will re-register this object for finalization)
                    // (the native GC has not reported we can remove this yet, so just flag the collection attempt [note: if 'Engine._Objects[_ID.Value].CanCollect' is true here, then finalize will succeed])
                }

            if (_Handle.IsWeakHandle) // (a handle is weak when there is only one reference [itself], which means this object is ready for the worker)
                _TryDisposeNativeHandle();
        }

        /// <summary>
        /// Called when there are no more references (on either the managed or native side) and the object is ready to be deleted from the V8.NET system.
        /// You should never call this from code directly unless you need to force the release of native resources associated with a custom implementation
        /// (and if so, a custom internal flag should be kept indicating whether or not the resources have been disposed).
        /// You should always override/implement this if you need to dispose of any native resources in custom implementations.
        /// DO NOT rely on the destructor (finalizer) - the object can still survive it.
        /// <para>Note: This can be triggered either by the worker thread, or on the call-back from the V8 garbage collector.  In either case, tread it as if
        /// it was called from the GC finalizer (not on the main thread).</para>
        /// *** If overriding, DON'T call back to this method, otherwise it will call back and end up in a cyclical call (and a stack overflow!). ***
        /// </summary>
        public virtual void Dispose()
        {
            if (_Proxy != this) _Proxy.Dispose();
        }

        /// <summary>
        /// This is called automatically when both the handle AND reference for the managed object are weak [no longer in use], in which case this object
        /// info instance is ready to be removed.
        /// </summary>
        internal void _TryDisposeNativeHandle()
        {
            if (IsManagedObjectWeak && (_Handle.IsEmpty || _Handle.IsWeakHandle && !_Handle.IsInPendingDisposalQueue))
            {
                _Handle.IsInPendingDisposalQueue = true; // (this also helps to make sure this method isn't called again)

                /*//??if ((Template == null || !(this is V8ManagedObject) && !(this is V8Function)) && !Engine._HasAccessors(_ID.Value)) // ('V8NativeObject' type objects don't have callbacks, but they might have accessors!)
                    _OnNativeGCRequested();
                else if (Template is ObjectTemplate && !((ObjectTemplate)Template).PropertyInterceptorsRegistered) // (no callback will occur if there are no interceptors, so just delete the entry now)
                    _OnNativeGCRequested();
                else*/

                lock (_Engine._WeakObjects)
                {
                    _Engine._WeakObjects.Add(_ID.Value); // (queue on the worker to set a native weak reference to dispose of this object later when the native GC is ready)
                }
            }
        }

        /// <summary>
        /// Called by the worker thread to make the native handle weak.  Once the native GC attempts to collect the underlying native object, then
        /// '_OnNativeGCRequested()' will get called to finalize the disposal of the managed object.
        /// </summary>
        internal void _MakeWeak()
        {
            V8NetProxy.MakeWeakHandle(_Handle);
            // (once the native V8 engine agrees, this instance will be removed due to a global GC callback registered when the engine was created)
        }

        internal bool _OnNativeGCRequested() // WARNING: The worker thread may trigger a V8 GC callback in its own thread!
        {
            lock (_Engine._Objects)
            {
                if (Template is FunctionTemplate)
                    ((FunctionTemplate)Template)._RemoveFunctionType(ID);// (make sure to remove the function references from the template instance)

                Dispose(); // (notify any custom dispose methods to clean up)

                GC.SuppressFinalize(this); // (required otherwise the object's finalizer will be triggered again)

                _Handle.Dispose(); // (note: this may already be disposed, in which case this call does nothing)

                Engine._ClearAccessors(_ID.Value); // (just to be sure - accessors are no longer needed once the native handle is GC'd)

                Template = null;

                if (_ID != null)
                    _Engine._RemoveObjectWeakReference(_ID.Value);
                _Handle.ObjectID = -1;
            }

            return true; // ("true" means to "continue disposal of native handle" [if not already empty])
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator InternalHandle(V8NativeObject obj) { return obj != null ? obj._Handle._Handle : InternalHandle.Empty; }
        public static implicit operator Handle(V8NativeObject obj) { return obj != null ? obj._Handle : ObjectHandle.Empty; }
        public static implicit operator ObjectHandle(V8NativeObject obj) { return obj != null ? obj._Handle : ObjectHandle.Empty; }

        // --------------------------------------------------------------------------------------------------------------------

        public override string ToString()
        {
            var objText = _Proxy.GetType().Name;
            var disposeText = _Handle.IsDisposed ? "Yes" : "No";
            return objText + " (ID: " + (_ID ?? -1) + " / Value: '" + _Handle + "' / Is Disposed?: " + disposeText + ")";
        }

        // --------------------------------------------------------------------------------------------------------------------

#if !(V1_1 || V2 || V3 || V3_5)
        public DynamicMetaObject GetMetaObject(Expression parameter)
        {
            return new DynamicHandle(this, parameter);
        }
#endif

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Returns true if successful.
        /// </summary>
        public virtual bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None)
        {
            return _Handle._Handle.SetProperty(name, value, attributes);
        }

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Returns true if successful.
        /// </summary>
        public virtual bool SetProperty(Int32 index, InternalHandle value)
        {
            return _Handle._Handle.SetProperty(index, value);
        }

        /// <summary>
        /// Sets a property to a given object. If the object is not V8.NET related, then the system will attempt to bind the instance and all public members to
        /// the specified property name.
        /// Returns true if successful.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="obj">Some value or object instance. 'Engine.CreateValue()' will be used to convert value types.</param>
        /// <param name="className">A custom in-script function name for the specified object type, or 'null' to use either the type name as is (the default) or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">For object instances, if true, then object reference members are included, otherwise only the object itself is bound and returned.
        /// For security reasons, public members that point to object instances will be ignored. This must be true to included those as well, effectively allowing
        /// in-script traversal of the object reference tree (so make sure this doesn't expose sensitive methods/properties/fields).</param>
        /// <param name="attributes">Flags that describe JavaScript properties.  They must be 'OR'd together as needed.</param>
        public bool SetProperty(string name, object obj, string className = null, bool recursive = false, V8PropertyAttributes attributes = V8PropertyAttributes.Undefined)
        {
            return _Handle._Handle.SetProperty(name, obj, className, recursive, attributes);
        }

        /// <summary>
        /// Binds a 'V8Function' object to the specified type and associates the type name (or custom script name) with the underlying object.
        /// Returns true if successful.
        /// </summary>
        /// <param name="type">The type to wrap.</param>
        /// <param name="className">A custom in-script function name for the specified type, or 'null' to use either the type name as is (the default) or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">For object types, if true, then object reference members are included, otherwise only the object itself is bound and returned.
        /// For security reasons, public members that point to object instances will be ignored. This must be true to included those as well, effectively allowing
        /// in-script traversal of the object reference tree (so make sure this doesn't expose sensitive methods/properties/fields).</param>
        /// <param name="attributes">Flags that describe JavaScript properties.  They must be 'OR'd together as needed.</param>
        public bool SetProperty(Type type, string className = null, bool recursive = false, V8PropertyAttributes attributes = V8PropertyAttributes.Undefined)
        {
            return _Handle._Handle.SetProperty(type, className, recursive, attributes);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(string name)
        {
            return _Handle._Handle.GetProperty(name);
        }

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(Int32 index)
        {
            return _Handle._Handle.GetProperty(index);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(string name)
        {
            return _Handle._Handle.GetProperty(name);
        }

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(Int32 index)
        {
            return _Handle._Handle.GetProperty(index);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object to create a property that is controlled by "getter" and "setter" callbacks.
        /// </summary>
        public void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter,
            V8PropertyAttributes attributes = V8PropertyAttributes.None, V8AccessControl access = V8AccessControl.Default)
        {
            _Handle._Handle.SetAccessor(name, getter, setter, attributes, access);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns a list of all property names for this object (including all objects in the prototype chain).
        /// </summary>
        public string[] GetPropertyNames()
        {
            return _Handle._Handle.GetPropertyNames();
        }

        /// <summary>
        /// Returns a list of all property names for this object (excluding the prototype chain).
        /// </summary>
        public virtual string[] GetOwnPropertyNames()
        {
            return _Handle._Handle.GetOwnPropertyNames();
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Get the attribute flags for a property of this object.
        /// If a property doesn't exist, then 'V8PropertyAttributes.None' is returned
        /// (Note: only V8 returns 'None'. The value 'Undefined' has an internal proxy meaning for property interception).</para>
        /// </summary>
        public V8PropertyAttributes GetPropertyAttributes(string name)
        {
            return _Handle._Handle.GetPropertyAttributes(name);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// The '_this' property is the "this" object within the function when called.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        public virtual InternalHandle Call(string functionName, InternalHandle _this, params InternalHandle[] args)
        {
            return _Handle._Handle.Call(functionName, _this, args);
        }

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        public InternalHandle Call(string functionName, params InternalHandle[] args)
        {
            return _Handle._Handle.Call(functionName, args);
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    /// <summary>
    /// This generic version of 'V8NativeObject' allows injecting your own class by implementing the 'IV8NativeObject' interface.
    /// </summary>
    /// <typeparam name="T">Your own class, which implements the 'IV8NativeObject' interface.  Don't use the generic version if you are able to inherit from 'V8NativeObject' instead.</typeparam>
    public unsafe class V8NativeObject<T> : V8NativeObject
        where T : IV8NativeObject, new()
    {
        // --------------------------------------------------------------------------------------------------------------------

        public V8NativeObject()
            : base(new T())
        {
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
