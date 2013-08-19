using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#if V2 || V3 || V3_5
#else
using System.Dynamic;
#endif

namespace V8.Net
{
    // ========================================================================================================================

    public unsafe partial class V8Engine
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// This object is the glue between native V8 objects, associated managed wrapper objects, and any associated template wrappers.
        /// The managed object wrappers for native V8 objects are stored with a weak reference so the custom V8.NET cleaner thread will be able to detect and remove the entry.
        /// </summary>
        internal class _ObjectInfo : IDisposable
        {
            internal V8Engine _Engine;
            internal int _ID;
            internal ITemplate _Template;
            internal bool _ManagedObjectIsInitilized;

            public IV8NativeObject ManagedObject
            {
                get
                {
                    IV8NativeObject mo = null;
                    if (_ManagedObject != null)
                        if (_ManagedObject.IsGCReady)
                            mo = _ManagedObject.Reset();
                        else
                            mo = _ManagedObject.Object;
                    return mo;
                }
                internal set { if (_ManagedObject != null) _ManagedObject.SetTarget(value); else _ManagedObject = new ObservableWeakReference<IV8NativeObject>(value); }
            }
            internal volatile ObservableWeakReference<IV8NativeObject> _ManagedObject;

            internal InternalHandle _Handle;

            public bool IsManagedObjectWeak { get { return (_ManagedObject == null || _ManagedObject.IsGCReady); } }

            ~_ObjectInfo() { _Handle.Dispose(); }

            /// <summary>
            /// Initializes the managed object associated with this object info.
            /// </summary>
            internal void Initialize()
            {
                if (_ManagedObjectIsInitilized) return;
                var mo = ManagedObject;
                if (mo != null) { mo.Initialize(); _ManagedObjectIsInitilized = true; }
            }

            internal _ObjectInfo(V8Engine engine, ITemplate template, IV8NativeObject mObj, InternalHandle hNObj)
            {
                _Engine = engine;
                _ID = -1;
                _Template = template;
                if (mObj != null) ManagedObject = mObj;
                _Handle.Set(hNObj);
            }

            public IV8NativeObject Prototype
            {
                get
                {
                    if (_Prototype == null && _Handle.IsObjectType)
                    {
                        // ... the prototype is not yet set, so get the prototype and wrap it ...
                        _Prototype = _Engine.GetObject<V8NativeObject>(_Handle, true, true);
                    }

                    return _Prototype;
                }
            }
            internal IV8NativeObject _Prototype; // (every object has a prototype, and this is it)

            /// <summary>
            /// This is called on the GC thread to flag that this managed object entry can be collected.
            /// <para> Note: There are no more managed references to the object at this point.</para>
            /// </summary>
            internal void _DoFinalize(IV8NativeObject obj)
            {
                _ManagedObject.DoFinalize(obj); // (native GC has not reported we can remove this yet, so just flag the collection attempt [note: if '_ManagedObject.CanCollect' is true here, then finalize will succeed])

                if (_Handle._IsWeakHandle) // (a handle is weak when there is only one reference [itself])
                    Dispose();
            }

            /// <summary>
            /// This is called automatically when both the handle AND reference for the managed object are weak [no longer in use], in which case this object
            /// info instance is ready to be removed.
            /// </summary>
            public void Dispose()
            {
                if (IsManagedObjectWeak && (_Handle.IsEmpty || _Handle._IsWeakHandle && !_Handle._HandleProxy->IsDisposeReady))
                {
                    _Handle._HandleProxy->IsDisposeReady = true;

                    IV8NativeObject mo = _ManagedObject != null ? _ManagedObject.Object : null;
                    if (_Template == null || mo is V8NativeObject && !(mo is V8ManagedObject) && !(mo is V8Function)) // (these types don't have callbacks, so just delete the entry now)
                        _OnNativeGCRequested();
                    else if (_Template is ObjectTemplate && !((ObjectTemplate)_Template)._PropertyInterceptorsRegistered) // (no callback will occur if there are no interceptors, so just delete the entry now)
                        _OnNativeGCRequested();
                    else
                        lock (_Engine._ObjectInfosToBeMadeWeak)
                        {
                            _Engine._ObjectInfosToBeMadeWeak.Add(_ID);
                        }
                }
            }

            internal void _MakeWeak()
            {
                V8NetProxy.MakeWeakHandle(_Handle); // (once the native V8 engine agrees, this instance will be removed)
            }

            internal bool _OnNativeGCRequested()
            {
                lock (_Engine._Objects)
                {
                    if (_ManagedObject != null)
                    {
                        var mo = _ManagedObject.Object;
                        if (mo != null)
                        {
                            if (_Template is FunctionTemplate)
                                ((FunctionTemplate)_Template)._RemoveFunctionType(_ID);// (make sure to remove the function references from the template instance)

                            mo.Dispose();

                            GC.SuppressFinalize(mo); // (required otherwise the object's finalizer will be triggered again)
                        }
                        _ManagedObject = null;
                    }

                    _Handle.Dispose(); // (note: this may already be disposed from "_DoFinalize()" above, in which case this call does nothing)
                    _Template = null;

                    _Engine._Objects.Remove(_ID);
                    _ID = -1;
                }
                return true; // ("true" means to "continue disposal of native handle" [if not already empty])
            }

            public override string ToString()
            {
                var objText = _ManagedObject.Object != null ? _ManagedObject.Object.GetType().Name : "{Managed Object Is Null}";
                var disposeText = _Handle.IsEmpty ? "{Handle Is Empty}" : _Handle;
                return objText + " (ID:" + _ID + ") / " + _Handle + " (Dispose: " + disposeText + ")";
            }
        }

        //internal void _ObjectInfosToBeMadeWeak() //??
        //{
        //    lock (Engine._ObjectInfosToBeMadeWeak)
        //    {
        //        Engine._ObjectInfosToBeMadeWeak.Remove(_id);
        //        _HandleProxy->IsDisposeReady = false;
        //    }
        //}

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds an index of all the created objects.
        /// </summary>
        internal readonly IndexedObjectList<_ObjectInfo> _Objects = new IndexedObjectList<_ObjectInfo>();

        // --------------------------------------------------------------------------------------------------------------------

        void _Initialize_ObjectTemplate()
        {
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// This MUST be called on managed objects before they become destructed.
        /// This helps to notify V8.NET when objects are ready to be garbage collected so that when the native side is also ready, both sides can be collected.
        /// </summary>
        public void Finalize(V8NativeObject v8NativeObject)
        {
            var objInfo = _Objects[v8NativeObject.ID];
            if (objInfo != null) objInfo._DoFinalize((IV8NativeObject)v8NativeObject);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object handle that wraps the native V8 handle for the specified managed object.
        /// <para>Warning: You MUST pass an object that was created from this V8Engine instance.</para>
        /// </summary>
        public InternalHandle GetObjectHandle(IV8NativeObject obj)
        {
            if (obj == null)
                return InternalHandle.Empty;

            if (obj.Engine != this)
                throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

            if (obj.ID < 0 || obj.ID >= _Objects.ObjectIndexListCount)
                throw new InvalidOperationException("The specified object (" + obj.GetType().Name + ") does not have a valid object ID.");

            return _Objects[obj.ID]._Handle;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object handle that wraps the native V8 handle for the specified managed object.
        /// <para>Warning: You MUST pass an object that was created from this V8Engine instance.</para>
        /// </summary>
        public void SetObjectHandle(IV8NativeObject obj, InternalHandle handle)
        {
            if (obj == null)
                return;

            if (!handle.IsObjectType)
                throw new InvalidOperationException("The specified handle does not represent a native object.");

            if (obj.Engine != this)
                throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

            if (obj.ID < 0 || obj.ID >= _Objects.ObjectIndexListCount)
                throw new InvalidOperationException("The specified object (" + obj.GetType().Name + ") does not have a valid object ID.");

            _Objects[obj.ID]._Handle.Set(handle);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object template that created the specified managed object.
        /// <para>Warning: You MUST pass an object that was created from this V8Engine instance.</para>
        /// </summary>
        public ITemplate GetObjectTemplate(IV8NativeObject obj)
        {
            if (obj == null)
                return null;

            if (obj.Engine != this)
                throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

            return _Objects[obj.ID]._Template;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object prototype for the specified object.
        /// <para>Warning: You MUST pass an object that was created from this V8Engine instance.</para>
        /// </summary>
        public IV8NativeObject GetObjectPrototype(IV8NativeObject obj)
        {
            if (obj == null)
                return null;

            if (obj.Engine != this)
                throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

            return _Objects[obj.ID].Prototype;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if the managed garbage collector is ready to collect the specified object (since no other CLR references exist).
        /// <para>Note: This is only for testing/debugging purposes.  As well, this call is only valid immediately after the last object reference is cleared,
        /// but before the next call to create a new object.</para>
        /// </summary>
        public bool IsObjectWeak(int objID)
        {
            var objectInfo = _Objects[objID];

            if (objectInfo == null)
                throw new InvalidOperationException("The specified object with ID '" + objID + "' was not found.");

            return objectInfo.IsManagedObjectWeak;
        }

        /// <summary>
        /// Returns true if the managed garbage collector is ready to collect the handle for the specified object (since no other CLR references exist).
        /// </summary>
        public bool IsHandleWeak(int handleID)
        {
            InternalHandle handle = _HandleProxies[handleID];

            if (handle.IsEmpty)
                throw new InvalidOperationException("The specified handle with ID '" + handleID + "' was not found.");

            return handle._IsWeakHandle;
        }

        ///// <summary>
        ///// Returns true if the specified object only has a weak reference left.
        ///// This can occur if there are no more handles in V8 to an object, which in turn triggers a call-back to clear the strong reference,
        ///// leaving only a weak one. The 'CanGarbageCollect()' virtual method is called on the managed object to determine if the strong reference should be
        ///// cleared. If an object only has a weak reference left, you can call 'RestoreStrongReference()' to undo it.
        ///// <para>When the managed side has no more references to an object with a weak reference, it will be destroyed.</para>
        ///// </summary>
        //??public bool IsWeakReferenced(IV8NativeObject obj)
        //{
        //    if (obj == null)
        //        return false;

        //    if (obj.V8Engine != this)
        //        throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

        //    return _Objects[obj.ID].__ManagedObjectStrongReference == null;
        //}

        ///// <summary>
        ///// Resets a strong reference to the managed object to keep it alive internally (which is the default behavior, unless V8 garbage collection clears it).
        ///// See <see cref="IsWeakReferenced"/> for more details.
        ///// </summary>
        //??public void RestoreStrongReference(IV8NativeObject obj)
        //{
        //    if (obj == null)
        //        return;

        //    if (obj.V8Engine != this)
        //        throw new InvalidOperationException("The specified object was not generated from this V8Engine instance.");

        //    var objInfo = _Objects[obj.ID];
        //    objInfo.__ManagedObjectStrongReference = objInfo.ManagedObject;
        //}

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates an uninitialized managed object ONLY (does not attempt to associate it with a JavaScript object, regardless of the supplied handle).
        /// <para>Warning: The managed wrapper is not yet initialized.  When returning the new managed object to the user, make sure to call
        /// '_ObjectInfo.Initialize()' first. Note however that new objects should only be initialized AFTER setup is completed so the users
        /// (developers) can write initialization code on completed objects (see source as example for 'FunctionTemplate.GetFunctionObject()').</para>
        /// </summary>
        /// <typeparam name="T">The wrapper type to create (such as V8ManagedObject).</typeparam>
        /// <param name="template">The managed template reference that owns the native object, if applicable.</param>
        /// <param name="handle">The handle to the native V8 object.</param>
        /// <param name="connectNativeObject">If true (the default), then a native function is called to associate the native V8 object with the new managed object.
        /// Set this to false if native V8 objects will be associated manually for special cases.  This parameter is ignored if no handle is given (hNObj == null).</param>
        internal _ObjectInfo _CreateManagedObject<T>(ITemplate template, InternalHandle handle, bool connectNativeObject = true)
                where T : class, IV8NativeObject, new()
        {
            _ObjectInfo objInfo;

            try
            {
                if (typeof(IV8ManagedObject).IsAssignableFrom(typeof(T)) && template == null)
                    throw new InvalidOperationException("You've attempted to create the type '" + typeof(T).Name + "' which implements IV8ManagedObject without a template (ObjectTemplate). The native V8 engine only supports interceptor hooks for objects generated from object templates.  You will need to first derive/implement V8NativeObject/IV8NativeObject, then wrap it around your object (or rewrite your object to implement V8NativeObject/IV8NativeObject instead and use the 'SetAccessor()' method).");

                if (!handle.IsUndefined)
                    if (!handle.IsObjectType)
                        throw new InvalidOperationException("The specified handle does not represent an native V8 object.");
                    else
                        if (connectNativeObject && handle.HasManagedObject)
                            throw new InvalidOperationException("Cannot create a managed object for this handle when one already exists. Existing objects will not be returned by 'Create???' methods to prevent initializing more than once.");

                var obj = new T();
                obj.Engine = this;
                objInfo = new _ObjectInfo(this, template, obj, handle.PassOn());
                lock (_Objects) // (need a lock because of the worker thread)
                {
                    objInfo._ID = _Objects.Add(objInfo);
                }
                obj.ID = objInfo._ID;

                if (!handle.IsUndefined)
                {
                    if (connectNativeObject)
                    {
                        handle.ManagedObjectID = objInfo._ID; // (just to keep the handle already on the managed side in sync as well [note: other handles will not be updated!])

                        try
                        {
                            void* templateProxy = (template is ObjectTemplate) ? (void*)((ObjectTemplate)template)._NativeObjectTemplateProxy :
                                (template is FunctionTemplate) ? (void*)((FunctionTemplate)template)._NativeFunctionTemplateProxy : null;

                            V8NetProxy.ConnectObject(handle, objInfo._ID, templateProxy);

                            /* The V8 object will have an associated internal field set to the index of the created managed object above for quick lookup.  This index is used
                             * to locate the associated managed object when a call-back occurs. The lookup is a fast O(1) operation using the custom 'IndexedObjectList' manager.
                             */
                        }
                        catch (Exception ex)
                        {
                            // ... something went wrong, so remove the new managed object ...
                            _Objects.Remove(objInfo._ID);
                            handle.ManagedObjectID = -1; // (existing ID no longer valid)
                            throw ex;
                        }
                    }
                }
            }
            finally
            {
                handle._DisposeIfFirst();
            }

            return objInfo;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the managed object that wraps the native V8 object for the specific handle.
        /// <para>Warning: You MUST pass a handle for objects only created from this V8Engine instance, otherwise you may get errors, or a wrong object (without error).</para>
        /// </summary>
        /// <typeparam name="T">You can derive your own object from V8NativeObject, or implement IV8NativeObject yourself.
        /// In either case, you can specify the type here to have it created for new object handles.</typeparam>
        /// <param name="handle">A handle to a native object that contains a valid managed object ID.</param>
        /// <param name="createIfNotFound">If true, then an IV8NativeObject of type 'T' will be created if an existing IV8NativeObject object cannot be found, otherwise 'null' is returned.</param>
        /// <param name="initializeOnCreate">If true (default) then then 'IV8NativeObject.Initialize()' is called on the created wrapper.</param>
        public T GetObject<T>(InternalHandle handle, bool createIfNotFound = true, bool initializeOnCreate = true)
            where T : class, IV8NativeObject, new()
        {
            return _GetObject<T>(null, handle, createIfNotFound, initializeOnCreate);
        }

        /// <summary>
        /// <see cref="GetObject&lt;T&gt;"/>
        /// </summary>
        public IV8NativeObject GetObject(InternalHandle handle, bool createIfNotFound = true, bool initializeOnCreate = true)
        { return GetObject<V8NativeObject>(handle, createIfNotFound, initializeOnCreate); }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Same as "GetObject()", but used internally for getting objects that are associated with templates (such as getting function prototype objects).
        /// </summary>
        internal T _GetObject<T>(ITemplate template, InternalHandle handle, bool createIfNotFound = true, bool initializeOnCreate = true, bool connectNativeObject = true)
            where T : class, IV8NativeObject, new()
        {
            IV8NativeObject obj = null;

            try
            {
                if (handle.IsEmpty)
                    return null;

                if (handle.Engine != this)
                    throw new InvalidOperationException("The specified handle was not generated from this V8Engine instance.");

                var objInfo = _Objects[handle.ManagedObjectID]; // (if out of bounds or invalid, this will simply return null)
                if (objInfo != null)
                {
                    obj = objInfo.ManagedObject;
                    if (obj != null && !typeof(T).IsAssignableFrom(obj.GetType()))
                        throw new InvalidCastException("The existing object of type '" + obj.GetType().Name + "' cannot be converted to type '" + typeof(T).Name + "'.");
                }

                if (obj == null && createIfNotFound)
                {
                    handle.ManagedObjectID = -1; // (managed object doesn't exist [perhaps GC'd], so reset the ID)
                    obj = _CreateObject<T>(template, handle.PassOn(), initializeOnCreate, connectNativeObject);
                }
            }
            finally
            {
                handle._DisposeIfFirst();
            }

            return (T)obj;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns an object based on its ID (an object ID is simply an index value, so the lookup is fast).
        /// <para>Note: If the ID is invalid, or the managed object has been garbage collected, then this will return null (no errors will occur).</para>
        /// <para>WARNING: Do not rely on this method unless you are sure the managed object is persisted. It's very possible for an object to be deleted and a
        /// new object put in the same place as identified by the same ID value. As long as you keep a reference/handle, or return 'false' in the
        /// 'CanGarbageCollect()' virtual method, then you can safely use this method.</para>
        /// </summary>
        public IV8NativeObject GetObjectByID(int objectID) { var oi = _Objects[objectID]; return oi != null ? oi.ManagedObject : null; }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
