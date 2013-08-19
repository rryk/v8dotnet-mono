using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if V2 || V3 || V3_5
#else
using System.Dynamic;
#endif

namespace V8.Net
{
    // ========================================================================================================================

    /// <summary>
    /// An interface for the V8NativeObject object.
    /// </summary>
    public interface IV8NativeObject : IDisposable
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the V8 engine that owns the object (once set, this should never change).
        /// </summary>
        V8Engine Engine { get; set; }

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object.
        /// This simply calls 'V8Engine.GetObjectHandle()' and 'V8Engine.SetObjectHandle()'.
        /// </summary>
        InternalHandle Handle { get; set; }

        /// <summary>
        /// The ID of this managed object within a V8Engine instance (once set, this should never change).
        /// </summary>
        int ID { get; set; }

#if V2 || V3 || V3_5
#else
        /// <summary>
        /// This normally is expected to just return "this as dynamic"; however, it also allows one to use some other dynamic object instead.
        /// If dynamic operations are not supported, this should return null.
        /// </summary>
        dynamic DynamicObject { get; }
#endif
        /// <summary>
        /// The prototype of the object (every JavaScript object implicitly has a prototype).
        /// This simple calls 'V8Engine.GetObjectPrototype()'.
        /// <para>Note: For basic objects not created by templates, the prototype is not set until
        /// 'V8Engine.GetObjectPrototype()' is called to retrieve it.</para>
        /// </summary>
        IV8NativeObject Prototype { get; }

        /// <summary>
        /// Returns the value/object handle associated with the specified property.
        /// </summary>
        InternalHandle this[string propertyName] { get; set; }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// <para>Note: Override "Dispose()" instead of implementing destructors (finalizers) if required.</para>
        /// </summary>
        void Initialize();

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Return true if successful. You can combine attribute flags using the bitwise OR operator ('|').
        /// </summary>
        bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None);

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Return true if successful.
        /// </summary>
        bool SetProperty(Int32 index, InternalHandle value);

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        InternalHandle GetProperty(string name);

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        InternalHandle GetProperty(Int32 index);

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        bool DeleteProperty(string name);

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        bool DeleteProperty(Int32 index);

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object.
        /// </summary>
        void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter,
            V8AccessControl access, V8PropertyAttributes attributes);

        /// <summary>
        /// Returns a list of all property names for this object (including all objects in the prototype chain).
        /// </summary>
        string[] GetPropertyNames();

        /// <summary>
        /// Returns a list of all property names for this object (excluding the prototype chain).
        /// </summary>
        string[] GetOwnPropertyNames();

        /// <summary>
        /// Get the attribute flags for a property of this object.
        /// If a property doesn't exist, then 'V8PropertyAttributes.None' is returned
        /// (Note: only V8 returns 'None'. The value 'Undefined' has an internal proxy meaning for property interception).</para>
        /// </summary>
        V8PropertyAttributes GetPropertyAttributes(string name);

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        InternalHandle Call(string functionName, params InternalHandle[] args);

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// The '_this' property is the "this" object within the function when called.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        InternalHandle Call(string functionName, InternalHandle _this, params InternalHandle[] args);

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// Represents a basic JavaScript object. This class wraps V8 functionality for operations required on any native V8 object (including managed ones).
    /// <para>This class implements 'DynamicObject' to make setting properties a bit easier.</para>
    /// </summary>
#if V2 || V3 || V3_5
    public unsafe class V8NativeObject : IV8NativeObject
#else
    public unsafe class V8NativeObject : DynamicObject, IV8NativeObject
#endif
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the V8Engine instance that owns this object.
        /// </summary>
        public V8Engine Engine { get; set; }

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object
        /// (this simply calls 'V8Engine.GetObjectHandle()').
        /// </summary>
        public InternalHandle Handle { get { return Engine.GetObjectHandle(this); } set { Engine.SetObjectHandle(this, value); } }

#if V2 || V3 || V3_5
#else
        /// <summary>
        /// Returns this object type cast as "dynamic".
        /// </summary>
        public virtual dynamic DynamicObject { get { return this as dynamic; } }
#endif

        /// <summary>
        /// The ID of this object within an ObjectTemplate instance.
        /// </summary>
        public int ID { get { return _ID; } set { if (_ID < 0) _ID = value; } }
        int _ID = -1;

        /// <summary>
        /// The prototype of the object (every JavaScript object implicitly has a prototype).
        /// </summary>
        public IV8NativeObject Prototype { get { return Engine.GetObjectPrototype(this); } }

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle this[string propertyName]
        {
            get
            {
                return GetProperty(propertyName);
            }
            set
            {
                SetProperty(propertyName, value);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// Derived objects should override this for construction instead of using the constructor.
        /// In the constructor, the object only exists as an empty shell not associated with anything.
        /// The danger is that any trigger to access the same object will cause a recursion situation (and thus a stack overflow error).
        /// It's ok to setup non-v8 values in constructors, but be careful not to trigger any calls into the V8Engine itself.
        /// </summary>
        public virtual void Initialize() { }

        ~V8NativeObject()
        {
            Engine.Finalize(this);
        }

        /// <summary>
        /// Called when there are no more handles and the object is ready to be disposed.
        /// You should always override this if you need to dispose of any native resources yourself.
        /// DO NOT rely on the destructor (finalizer).
        /// <para>Note: This can be called from the finalizer thread (indirectly through V8.NET), in which case
        /// 'GC.SuppressFinalize()' will be called automatically upon return.</para>
        /// </summary>
        public virtual void Dispose() { }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator InternalHandle(V8NativeObject obj) { return obj != null ? obj.Handle : InternalHandle.Empty; }

        // --------------------------------------------------------------------------------------------------------------------
        // DynamicObject support is in .NET 4.0 and higher

#if V2 || V3 || V3_5
#else

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return GetPropertyNames();
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var handle = GetProperty(binder.Name);
            if (handle.HasManagedObject)
                result = handle.ManagedObject; // (for objects, return the instance instead of a value handle)
            else
                result = handle.Value;
            return !handle.IsUndefined;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            var propertyName = binder.Name;

            InternalHandle _value = null;

            if (value is Handle)
                _value = (InternalHandle)value; // (objects are detected and added directly! ;))
            if (value is InternalHandle)
                _value = (InternalHandle)value;
            else if (value is InternalHandle)
                _value = (InternalHandle)value;
            else if (value is IV8NativeObject)
                _value = ((IV8NativeObject)value).Handle; // (objects are detected and added directly! ;))
            else if (value is IJSProperty)
                _value = ((IJSProperty)value).Value; // (IJSProperty values are detected and set directly! ;))
            else
                _value = Engine.CreateNativeValue(value);

            return SetProperty(propertyName, _value);
        }

        public override bool TryDeleteMember(DeleteMemberBinder binder)
        {
            return DeleteProperty(binder.Name);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            result = null;

            if (indexes.Length == 1)
            {
                var o = indexes[0];
                InternalHandle valueHandle = null;

                if (o is Int16 || o is Int32 || o is Int64) // TODO: Test this.
                    valueHandle = GetProperty((Int32)o);
                else if (o != null)
                    valueHandle = GetProperty(o.ToString());

                if (valueHandle != null)
                    result = valueHandle.Value;
            }

            return false;
        }

#endif

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Return true if successful.
        /// </summary>
        public virtual bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None)
        {
            try
            {
                return V8NetProxy.SetObjectPropertyByName(Handle, name, value, attributes);
            }
            finally
            {
                value._DisposeIfFirst();
            }
        }

        /// <summary>
        /// Calls the V8 'Set()' function on the underlying native object.
        /// Return true if successful.
        /// </summary>
        public virtual bool SetProperty(Int32 index, InternalHandle value)
        {
            try
            {
                return V8NetProxy.SetObjectPropertyByIndex(Handle, index, value);
            }
            finally
            {
                value._DisposeIfFirst();
            }
        }

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(string name)
        {
            return V8NetProxy.GetObjectPropertyByName(Handle, name);
        }

        /// <summary>
        /// Calls the V8 'Get()' function on the underlying native object.
        /// If the property doesn't exist, the 'IsUndefined' property will be true.
        /// </summary>
        public virtual InternalHandle GetProperty(Int32 index)
        {
            return V8NetProxy.GetObjectPropertyByIndex(Handle, index);
        }

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(string name)
        {
            return V8NetProxy.DeleteObjectPropertyByName(Handle, name);
        }

        /// <summary>
        /// Calls the V8 'Delete()' function on the underlying native object.
        /// Returns true if the property was deleted.
        /// </summary>
        public virtual bool DeleteProperty(Int32 index)
        {
            return V8NetProxy.DeleteObjectPropertyByIndex(Handle, index);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls the V8 'SetAccessor()' function on the underlying native object to create a property that is controlled by "getter" and "setter" callbacks.
        /// </summary>
        public void SetAccessor(string name,
            V8NativeObjectPropertyGetter getter, V8NativeObjectPropertySetter setter,
            V8AccessControl access, V8PropertyAttributes attributes)
        {
            V8NetProxy.SetObjectAccessor(Handle, ID, name,
                   (HandleProxy* _this, string propertyName) =>
                   {
                       using (InternalHandle hThis = _this) { return getter != null ? getter(hThis, propertyName) : null; }
                   },
                   (HandleProxy* _this, string propertyName, HandleProxy* value) =>
                   {
                       if (setter != null)
                           using (InternalHandle hThis = _this) { setter(hThis, propertyName, value); }
                   },
                   access, attributes);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns a list of all property names for this object (including all objects in the prototype chain).
        /// </summary>
        public string[] GetPropertyNames()
        {
            using (InternalHandle v8array = V8NetProxy.GetPropertyNames(Handle))
            {
                var length = V8NetProxy.GetArrayLength(v8array);

                var names = new string[length];

                InternalHandle itemHandle;

                for (var i = 0; i < length; i++)
                    using (itemHandle = V8NetProxy.GetObjectPropertyByIndex(v8array, i))
                    {
                        names[i] = itemHandle;
                    }

                return names;
            }
        }

        /// <summary>
        /// Returns a list of all property names for this object (excluding the prototype chain).
        /// </summary>
        public virtual string[] GetOwnPropertyNames()
        {
            using (InternalHandle v8array = V8NetProxy.GetOwnPropertyNames(Handle))
            {
                var length = V8NetProxy.GetArrayLength(v8array);

                var names = new string[length];

                InternalHandle itemHandle;

                for (var i = 0; i < length; i++)
                    using (itemHandle = V8NetProxy.GetObjectPropertyByIndex(v8array, i))
                    {
                        names[i] = itemHandle;
                    }

                return names;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Get the attribute flags for a property of this object.
        /// If a property doesn't exist, then 'V8PropertyAttributes.None' is returned
        /// (Note: only V8 returns 'None'. The value 'Undefined' has an internal proxy meaning for property interception).</para>
        /// </summary>
        public V8PropertyAttributes GetPropertyAttributes(string name)
        {
            return V8NetProxy.GetPropertyAttributes(Handle, name);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// The '_this' property is the "this" object within the function when called.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        public virtual InternalHandle Call(string functionName, InternalHandle _this, params InternalHandle[] args)
        {
            try
            {
                HandleProxy** nativeArrayMem = Utilities.MakeHandleProxyArray(args);

                var result = V8NetProxy.Call(Handle, functionName, _this, args.Length, nativeArrayMem);

                Utilities.FreeNativeMemory((IntPtr)nativeArrayMem);

                return result;
            }
            finally
            {
                _this._DisposeIfFirst();
            }
        }

        /// <summary>
        /// Calls an object property with a given name on a specified object as a function and returns the result.
        /// If the function name is null or empty, then the current object is assumed to be a function object.
        /// </summary>
        public InternalHandle Call(string functionName, params InternalHandle[] args)
        {
            return Call(functionName, InternalHandle.Empty, args);
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// Intercepts JavaScript access for properties on the associated JavaScript object for retrieving a value.
    /// <para>To allow the V8 engine to perform the default get action, return "Handle.Empty".</para>
    /// </summary>
    public delegate InternalHandle V8NativeObjectPropertyGetter(InternalHandle _this, string propertyName);

    /// <summary>
    /// Intercepts JavaScript access for properties on the associated JavaScript object for setting values.
    /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
    /// </summary>
    public delegate void V8NativeObjectPropertySetter(InternalHandle _this, string propertyName, InternalHandle value);

    // ========================================================================================================================
}
