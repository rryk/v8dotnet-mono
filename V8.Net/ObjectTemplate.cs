﻿using System;
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

    public interface ITemplate
    {
        /// <summary>
        /// The V8Engine instance associated with this template.
        /// </summary>
        V8Engine Engine { get; }
    }

    internal interface ITemplateInternal
    {
        uint _ReferenceCount { get; set; }
    }

    // ========================================================================================================================

    public unsafe abstract class TemplateBase<ObjectType> : ITemplate, ITemplateInternal where ObjectType : class, IV8NativeObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        public V8Engine Engine { get { return _Engine; } }
        internal V8Engine _Engine;


        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the parent to this template, if any.
        /// This is currently only set on object template instances associated with function templates (where {FunctionTemplate} is the parent).
        /// </summary>
        public ITemplate Parent
        {
            get { return _Parent; }
            internal set
            {
                if (_Parent != null) ((ITemplateInternal)this)._ReferenceCount--;
                _Parent = value;
                if (_Parent != null) ((ITemplateInternal)this)._ReferenceCount++;
            }
        }
        ITemplate _Parent;

        /// <summary>
        /// The number of objects that reference this object.
        /// This is required because of the way the GC resets all weak references to null, and finalizes in no special order.
        /// Dependent objects are required to update this when they are finally collected (as some may become re-registered with the finalizer).
        /// </summary>
        uint ITemplateInternal._ReferenceCount { get; set; }

        // --------------------------------------------------------------------------------------------------------------------

        protected List<Delegate> _NativeCallbacks; // (if not stored here, then delegates will be GC'd and callbacks from native code will fail)

        /// <summary>
        /// Keeps callback delegates alive.
        /// <para>If delegates are used as callbacks (for reverse P/Invoke), then they will become GC'd if there's no managed reference keeping them alive.</para>
        /// </summary>
        protected T _SetDelegate<T>(T d)
        {
            if (_NativeCallbacks == null)
                _NativeCallbacks = new List<Delegate>();

            _NativeCallbacks.Add((Delegate)(object)d);

            return d;
        }

        // --------------------------------------------------------------------------------------------------------------------

        public TemplateBase()
        {
        }

        // --------------------------------------------------------------------------------------------------------------------

        protected HandleProxy* _NamedPropertyGetter(string propertyName, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return null;
            var mo = obj.Reset() as IV8ManagedObject; // (this acts also as a test because native object wrappers are also supported)
            return mo != null ? mo.NamedPropertyGetter(ref propertyName) : null;
        }

        protected HandleProxy* _NamedPropertySetter(string propertyName, HandleProxy* value, ref ManagedAccessorInfo info)
        {
            using (InternalHandle hValue = new InternalHandle(value, false))
            {
                var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject;
                return mo != null ? mo.NamedPropertySetter(ref propertyName, hValue, V8PropertyAttributes.Undefined) : null;
            }
        }

        protected V8PropertyAttributes _NamedPropertyQuery(string propertyName, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return V8PropertyAttributes.Undefined;
            var mo = obj.Reset() as IV8ManagedObject;
            var result = mo != null ? mo.NamedPropertyQuery(ref propertyName) : null;
            if (result != null) return result.Value;
            else return V8PropertyAttributes.Undefined; // (not intercepted, so perform default action)
        }

        protected int _NamedPropertyDeleter(string propertyName, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return -1;
            var mo = obj.Reset() as IV8ManagedObject;
            var result = mo != null ? mo.NamedPropertyDeleter(ref propertyName) : null;
            if (result != null) return result.Value ? 1 : 0;
            else return -1; // (not intercepted, so perform default action)
        }

        protected HandleProxy* _NamedPropertyEnumerator(ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return null;
            var mo = obj.Reset() as IV8ManagedObject;
            return mo != null ? mo.NamedPropertyEnumerator() : null;
        }

        // --------------------------------------------------------------------------------------------------------------------

        protected HandleProxy* _IndexedPropertyGetter(Int32 index, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return null;
            var mo = obj.Reset() as IV8ManagedObject;
            return mo != null ? mo.IndexedPropertyGetter(index) : null;
        }

        protected HandleProxy* _IndexedPropertySetter(Int32 index, HandleProxy* value, ref ManagedAccessorInfo info)
        {
            using (InternalHandle hValue = new InternalHandle(value, false))
            {
                var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
                if (obj == null)
                    return null;
                var mo = obj.Reset() as IV8ManagedObject;
                return mo != null ? mo.IndexedPropertySetter(index, hValue) : null;
            }
        }

        protected V8PropertyAttributes _IndexedPropertyQuery(Int32 index, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return V8PropertyAttributes.Undefined;
            var mo = obj.Reset() as IV8ManagedObject;
            var result = mo != null ? mo.IndexedPropertyQuery(index) : null;
            if (result != null) return result.Value;
            else return V8PropertyAttributes.Undefined; // (not intercepted, so perform default action)
        }

        protected int _IndexedPropertyDeleter(Int32 index, ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return -1;
            var mo = obj.Reset() as IV8ManagedObject;
            var result = mo != null ? mo.IndexedPropertyDeleter(index) : null;
            if (result != null) return result.Value ? 1 : 0;
            else return -1; // (not intercepted, so perform default action)
        }

        protected HandleProxy* _IndexedPropertyEnumerator(ref ManagedAccessorInfo info)
        {
            var obj = _Engine._GetObjectWeakReference(info.ManagedObjectID);
            if (obj == null)
                return null;
            var mo = obj.Reset() as IV8ManagedObject;
            return mo != null ? mo.IndexedPropertyEnumerator() : null;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    public unsafe class ObjectTemplate : TemplateBase<IV8ManagedObject>
    {
        // --------------------------------------------------------------------------------------------------------------------

        internal NativeObjectTemplateProxy* _NativeObjectTemplateProxy;

        // --------------------------------------------------------------------------------------------------------------------

        public ObjectTemplate()
        {
            if (Assembly.GetCallingAssembly() != Assembly.GetExecutingAssembly() && !Assembly.GetCallingAssembly().FullName.StartsWith("mscorlib,"))
                throw new InvalidOperationException("You must create object templates by calling 'V8Engine.CreateObjectTemplate()'.");
        }

        internal void _Initialize(V8Engine v8EngineProxy, bool registerPropertyInterceptors = true)
        {
            _Initialize(v8EngineProxy,
                (NativeObjectTemplateProxy*)V8NetProxy.CreateObjectTemplateProxy(v8EngineProxy._NativeV8EngineProxy)// (create a corresponding native object)
            );
        }

        internal void _Initialize(V8Engine v8EngineProxy, NativeObjectTemplateProxy* nativeObjectTemplateProxy, bool registerPropertyInterceptors = true)
        {
            if (v8EngineProxy == null)
                throw new ArgumentNullException("v8EngineProxy");

            if (nativeObjectTemplateProxy == null)
                throw new ArgumentNullException("nativeObjectTemplateProxy");

            _Engine = v8EngineProxy;

            _NativeObjectTemplateProxy = nativeObjectTemplateProxy;

            if (registerPropertyInterceptors)
                RegisterPropertyInterceptors();
        }

        ~ObjectTemplate()
        {
            if (((ITemplateInternal)this)._ReferenceCount > 0
                || _Engine.GetObjects(this).Length > 0
                || Parent != null && _Engine.GetObjects(Parent).Length > 0)
                GC.ReRegisterForFinalize(this);
            else
                Dispose();
        }

        public void Dispose() // TODO: !!! This will cause issues if removed while the native object exists. !!!
        {
            if (_NativeObjectTemplateProxy != null)
            {
                V8NetProxy.DeleteObjectTemplateProxy(_NativeObjectTemplateProxy); // (delete the corresponding native object as well; WARNING: This is done on the GC thread!)
                _NativeObjectTemplateProxy = null;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this function template has any interceptors (callbacks) registered.
        /// </summary>
        public bool PropertyInterceptorsRegistered { get; internal set; }

        /// <summary>
        /// Registers handlers that intercept access to properties on ALL objects created by this template.  The native V8 engine only supports this on 'ObjectTemplate's.
        /// </summary>
        public void RegisterPropertyInterceptors()
        {
            if (!PropertyInterceptorsRegistered)
            {
                V8NetProxy.RegisterNamedPropertyHandlers(_NativeObjectTemplateProxy,
                    _SetDelegate<ManagedNamedPropertyGetter>(_NamedPropertyGetter),
                    _SetDelegate<ManagedNamedPropertySetter>(_NamedPropertySetter),
                    _SetDelegate<ManagedNamedPropertyQuery>(_NamedPropertyQuery),
                    _SetDelegate<ManagedNamedPropertyDeleter>(_NamedPropertyDeleter),
                    _SetDelegate<ManagedNamedPropertyEnumerator>(_NamedPropertyEnumerator));

                V8NetProxy.RegisterIndexedPropertyHandlers(_NativeObjectTemplateProxy,
                    _SetDelegate<ManagedIndexedPropertyGetter>(_IndexedPropertyGetter),
                    _SetDelegate<ManagedIndexedPropertySetter>(_IndexedPropertySetter),
                    _SetDelegate<ManagedIndexedPropertyQuery>(_IndexedPropertyQuery),
                    _SetDelegate<ManagedIndexedPropertyDeleter>(_IndexedPropertyDeleter),
                    _SetDelegate<ManagedIndexedPropertyEnumerator>(_IndexedPropertyEnumerator));

                PropertyInterceptorsRegistered = true;
            }
        }

        /// <summary>
        /// Unregisters handlers that intercept access to properties on ALL objects created by this template.  See <see cref="RegisterPropertyInterceptors()"/>.
        /// </summary>
        public void UnregisterPropertyInterceptors()
        {
            if (PropertyInterceptorsRegistered)
            {
                V8NetProxy.UnregisterNamedPropertyHandlers(_NativeObjectTemplateProxy);

                V8NetProxy.UnregisterIndexedPropertyHandlers(_NativeObjectTemplateProxy);

                PropertyInterceptorsRegistered = false;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates an object of the specified type and returns it.  A V8 object is also created and associated with it.
        /// <para>Performance note: Creating 'V8NativeObject' type objects are allowed, but an object template is not needed for those.  If you create a
        /// 'V8NativeObject' object from a template, it simply wraps the native object create by the template, and property interceptors (call-backs) are still
        /// triggered.  While native objects are faster than managed ones, creating 'V8NativeObject' objects using 'V8Engine.CreateObject()' does not use
        /// interceptors and is many times faster than template objects.  If it is desired to create 'V8NativeObject' objects from templates, consider calling
        /// '<seealso cref="UnregisterPropertyInterceptors()"/>' on the object template to make them the same speed as if 'V8Engine.CreateObject()' was used.</para>
        /// </summary>
        /// <typeparam name="T">The type of managed object to create, which must implement 'IV8NativeObject',</typeparam>
        public T CreateObject<T>() where T : V8NativeObject, new()
        {
            if (_NativeObjectTemplateProxy == null)
                throw new InvalidOperationException("This managed object template is either not initialized, or does not support creating V8 objects.");

            // ... create object locally first and index it ...

            var obj = _Engine._CreateManagedObject<T>(this, InternalHandle.Empty);

            // ... create the native object and associated it to the managed wrapper ...

            try
            {
                obj.Handle._Set(V8NetProxy.CreateObjectFromTemplate(_NativeObjectTemplateProxy, obj.ID));
                // (note: setting '_NativeObject' also updates it's '_ManagedObject' field if necessary.
            }
            catch (Exception ex)
            {
                // ... something went wrong, so remove the new managed object ...
                _Engine._RemoveObjectWeakReference(obj.ID);
                throw ex;
            }

            obj.Initialize();

            return (T)obj;
        }

        /// <summary>
        /// See <see cref="CreateObject<T>()"/>.
        /// </summary>
        public V8ManagedObject CreateObject() { return CreateObject<V8ManagedObject>(); }

        // ========================================================================================================================
    }
}
