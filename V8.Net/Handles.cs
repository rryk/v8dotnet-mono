using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace V8.Net
{
    // ========================================================================================================================

    /// <summary>
    /// Keeps track of native V8 handles (C++ native side).
    /// When no more handles are in use, the native handle can be disposed when the V8.NET system is ready.
    /// If the handle is a value, the native handle side is disposed immediately - but if the handle represents a managed object, it waits until the managed
    /// object is also no longer in use.
    /// <para>Handles are very small values that can be passed around quickly on the stack, and as a result, the garbage collector is not involved as much.
    /// This helps prevent the GC from kicking in and slowing down applications when a lot of processing is in effect.
    /// Another benefit is that thread locking is required for heap memory allocation (for obvious reasons), so stack allocation is faster within a
    /// multi-threaded context.</para>
    /// </summary>
    public unsafe class Handle : IDisposable
    {
        // --------------------------------------------------------------------------------------------------------------------

        public static readonly Handle Empty = new Handle((HandleProxy*)null);

        // --------------------------------------------------------------------------------------------------------------------

        internal HandleProxy* _HandleProxy; // ('HandleInfo' or 'WeakReference<HandleInfo>' type only, or 'null' for empty/undefined handles)

        // --------------------------------------------------------------------------------------------------------------------

        internal Handle(HandleProxy* hp)
        {
            Set(hp);
        }

        public Handle(InternalHandle handle)
        {
            Set(handle);
        }

        ~Handle()
        {
            if (_HandleProxy != null)
                _Dispose(true);
        }

        /// <summary>
        /// Disposes of the current handle proxy reference (if not empty, and different) and replaces it with the specified new reference.
        /// <para>Note: This IS REQUIRED when setting handles, otherwise memory leaks may occur (the native V8 handles will never make it back into the cache).
        /// NEVER use the "=" operator to set a handle.  If using 'InternalHandle' handles, ALWAYS call "Dispose()" when they are no longer needed.
        /// To be safe, use the "using(SomeInternalHandle){}" statement (with 'InternalHandle' handles), or use "Handle refHandle = SomeInternalHandle;", to
        /// to convert it to a handle object that will dispose itself.</para>
        /// </summary>
        public Handle Set(InternalHandle handle)
        {
            if (handle._First)
            {
                var h = Set(handle._HandleProxy);
                handle.Dispose(); // Disposes the handle if it is the first one (the first one is disposed automatically when passed back into the engine).
                return h;
            }
            else return Set(handle._HandleProxy);
        }

        internal Handle Set(HandleProxy* hp)
        {
            if (_HandleProxy != hp)
            {
                bool reRegisterForFinalize = false;

                if (_HandleProxy != null)
                {
                    Dispose();
                    reRegisterForFinalize = true;
                }

                _HandleProxy = hp;

                if (_HandleProxy != null)
                {
                    // ... verify the native handle proxy ID is within a valid range before storing it, and resize as needed ...

                    var engine = V8Engine._Engines[_HandleProxy->EngineID];
                    var handleID = _HandleProxy->ID;

                    if (handleID >= engine._HandleProxies.Length)
                    {
                        HandleProxy*[] handleProxies = new HandleProxy*[(100 + handleID) * 2];
                        for (var i = 0; i < engine._HandleProxies.Length; i++)
                            handleProxies[i] = engine._HandleProxies[i];
                        engine._HandleProxies = handleProxies;
                    }

                    engine._HandleProxies[handleID] = _HandleProxy;

                    _HandleProxy->ManagedReferenceCount++;

                    if (reRegisterForFinalize)
                        GC.ReRegisterForFinalize(this);
                    GC.AddMemoryPressure((Marshal.SizeOf(typeof(HandleProxy))));
                }
            }
            return this;
        }

        /// <summary>
        /// Handles should be set in either two ways: 1. by using the "Set()" method on the left side handle, or 2. using the "Clone()' method on the right side.
        /// Using the "=" operator to set a handle may cause memory leaks if not used correctly.
        /// See also: <seealso cref="Set()"/>
        /// </summary>
        public Handle Clone()
        {
            return new Handle(_HandleProxy);
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _Dispose(bool inFinalizer)
        {
            if (_HandleProxy != null && _HandleProxy->ManagedReferenceCount > 0)
            {
                _HandleProxy->ManagedReferenceCount--;

                if (_HandleProxy->ManagedReferenceCount == 0)
                    __TryDispose();
                else
                    _HandleProxy = null;
            }

            if (!inFinalizer)
                GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Attempts to dispose of the internally wrapped handle proxy and makes this handle empty.
        /// If other handles exist, then they will still be valid, and this handle instance will become empty.
        /// <para>This is useful to use with "using" statements to quickly release a handle into the cache for reuse.</para>
        /// </summary>
        public void Dispose() { _Dispose(false); }

        /// <summary>
        /// Returns true if this handle is disposed (no longer in use).  Disposed handles are kept in a cache for performance reasons.
        /// </summary>
        public bool IsDisposed { get { return _HandleProxy == null || _HandleProxy->IsDisposed; } }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator Handle(HandleProxy* hp)
        {
            return hp != null ? new Handle(hp) : Handle.Empty;
        }

        public static implicit operator InternalHandle(Handle handle)
        {
            return handle != null ? new InternalHandle(handle._HandleProxy) : InternalHandle.Empty;
        }

        public static implicit operator V8NativeObject(Handle handle)
        {
            return handle.ManagedObject as V8NativeObject;
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static bool operator ==(Handle h1, Handle h2)
        {
            return (object)h1 == (object)h2 || (object)h1 != null && h1.Equals(h2);
        }

        public static bool operator !=(Handle h1, Handle h2)
        {
            return !(h1 == h2);
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator HandleProxy*(Handle handle)
        {
            return handle != null ? handle._HandleProxy : null;
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator bool(Handle handle)
        {
            return (bool)Types.ChangeType(handle.Value, typeof(bool));
        }

        public static implicit operator Int32(Handle handle)
        {
            return (Int32)Types.ChangeType(handle.Value, typeof(Int32));
        }

        public static implicit operator double(Handle handle)
        {
            return (double)Types.ChangeType(handle.Value, typeof(double));
        }

        public static implicit operator string(Handle handle)
        {
            var val = handle.Value;
            if (val == null) return "";
            return (string)Types.ChangeType(val, typeof(string));
        }

        public static implicit operator DateTime(Handle handle)
        {
            var ms = (double)Types.ChangeType(handle.Value, typeof(double));
            return new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(ms);
        }

        public static implicit operator JSProperty(Handle handle)
        {
            return new JSProperty(handle);
        }

        // --------------------------------------------------------------------------------------------------------------------
        #region ### SHARED HANDLE CODE START ###

        /// <summary>
        /// The ID (index) of this handle on both the native and managed sides.
        /// </summary>
        public int ID { get { return _HandleProxy != null ? _HandleProxy->ID : -1; } }

        /// <summary>
        /// The JavaScript type this handle represents.
        /// </summary>
        public JSValueType ValueType { get { return _HandleProxy != null ? _HandleProxy->_ValueType : JSValueType.Undefined; } }

        /// <summary>
        /// Used internally to determine the number of references to a handle.
        /// </summary>
        public Int64 ReferenceCount { get { return _HandleProxy != null ? _HandleProxy->ManagedReferenceCount : 0; } }

        /// <summary>
        /// A reference to the V8Engine instance that owns this handle.
        /// </summary>
        public V8Engine Engine { get { return _HandleProxy != null ? V8Engine._Engines[_HandleProxy->EngineID] : null; } }

        // --------------------------------------------------------------------------------------------------------------------
        // Managed Object Properties and References

        /// <summary>
        /// The ID of the managed object represented by this handle.
        /// This ID is expected when handles are passed to 'V8ManagedObject.GetObject()'.
        /// If this value is less than 0 (usually -1), then there is no associated managed object.
        /// </summary>
        public Int32 ManagedObjectID
        {
            get { return _HandleProxy == null ? -1 : _HandleProxy->_ManagedObjectID >= 0 ? _HandleProxy->_ManagedObjectID : V8NetProxy.GetHandleManagedObjectID(_HandleProxy); }
            internal set { if (_HandleProxy != null) _HandleProxy->_ManagedObjectID = value; }
        }

        /// <summary>
        /// Returns the managed object ID "as is".
        /// </summary>
        internal Int32 _ManagedObjectID
        {
            get { return _HandleProxy != null ? _HandleProxy->_ManagedObjectID : -1; }
            set { if (_HandleProxy != null) _HandleProxy->_ManagedObjectID = value; }
        }

        /// <summary>
        /// A reference to the managed object associated with this handle. This property is only valid for object handles, and will return null otherwise.
        /// Upon reading this property, if the managed object has been garbage collected (because no more handles or references exist), then a new basic 'V8NativeObject' instance will be created.
        /// <para>Instead of checking for 'null' (which may not work as expected), query 'HasManagedObject' instead.</para>
        /// </summary>
        public IV8NativeObject ManagedObject
        {
            get
            {
                if (HasManagedObject)
                {
                    return Engine._Objects[_HandleProxy->_ManagedObjectID].ManagedObject;
                }
                else return null;
            }
        }

        /// <summary>
        /// Returns true if this handle is associated with a managed object.
        /// <para>Note: This can be false even though 'IsObjectType' may be true.
        /// A handle can represent a native V8 object handle without requiring an associated managed object.</para>
        /// </summary>
        public bool HasManagedObject
        {
            get
            {
                if (IsObjectType && ManagedObjectID >= 0)
                {
                    var managedObjectInfo = Engine._Objects[_ManagedObjectID];
                    return (managedObjectInfo != null && managedObjectInfo._ManagedObject != null && managedObjectInfo._ManagedObject.Object != null);
                }
                else return false;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Reading from this property causes a native call to fetch the current V8 value associated with this handle.
        /// </summary>
        public object Value { get { if (_HandleProxy != null) { V8NetProxy.UpdateHandleValue(_HandleProxy); return _HandleProxy->Value; } else return null; } }

        /// <summary>
        /// Once "Value" is accessed to retrieve the JavaScript value in real time, there's no need to keep accessing it.  Just call this property
        /// instead (a small bit faster). Note: If the value changes again within the engine (i.e. another scripts executes), you may need to call
        /// 'Value' again to make sure any changes are reflected.
        /// </summary>
        public object LastValue
        {
            get
            {
                return _HandleProxy == null ? null : ((int)_HandleProxy->_ValueType) >= 0 ? _HandleProxy->Value : Value;
            }
        }

        /// <summary>
        /// Returns the array length for handles that represent arrays. For all other types, this returns 0.
        /// </summary>
        public Int32 ArrayLength { get { return IsArray ? V8NetProxy.GetArrayLength(_HandleProxy) : 0; } }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle is associated with a managed object that has no other references and is ready to be disposed.
        /// </summary>
        internal bool _IsWeakManagedObject
        {
            get
            {
                var id = _ManagedObjectID;
                if (id >= 0)
                {
                    var engine = Engine;
                    lock (engine._Objects)
                    {
                        var objInfo = engine._Objects[_ManagedObjectID];
                        if (objInfo != null)
                            return objInfo.IsManagedObjectWeak;
                        else
                            _ManagedObjectID = id = -1; // (this ID is no longer valid)
                    }
                }
                return id == -1;
            }
        }

        /// <summary>
        /// Returns true if the handle is weak and ready to be disposed.
        /// </summary>
        internal bool _IsWeakHandle { get { return _HandleProxy == null || _HandleProxy->ManagedReferenceCount <= 1; } }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// True if this handle is ready to be disposed (cached) on the native side.
        /// </summary>
        internal bool _IsInPendingDisposalQueue { get { return _HandleProxy != null && _HandleProxy->IsDisposeReady; } }

        /// <summary>
        /// Returns true if this handle is weak AND is associated with a weak managed object reference.
        /// When a handle is ready to be disposed, then calling "Dispose()" will succeed and cause the handle to be placed back into the cache on the native side.
        /// </summary>
        internal bool _IsDisposeReady { get { return _IsWeakHandle && _IsWeakManagedObject; } }

        /// <summary>
        /// Attempts to dispose of this handle (add it back into the native proxy cache for reuse).  If the handle represents a managed object,
        /// the dispose request is placed into a "pending disposal" queue. When the associated managed object
        /// no longer has any references, this method will be .
        /// <para>*** NOTE: This is called by Dispose() when the reference count becomes zero and should not be called directly. ***</para>
        /// </summary>
        internal bool __TryDispose()
        {
            if (_IsDisposeReady)
            {
                _HandleProxy->IsDisposeReady = true;
                _CompleteDisposal(); // (no need to wait! there's no managed object.)
                return true;

                //??var hasManagedObject = _ManagedObjectID >= 0;

                //??if (!hasManagedObject || _IsWeakManagedObject)
                //{
                //    _CompleteDisposal(); // (no need to wait! there's no managed object.)
                //    return true;
                //}
                //else if (hasManagedObject) // (managed side not yet ready)
                //{
                //    lock (Engine._HandleProxiesPendingDisposal)
                //    {
                //        _HandleProxy->IsDisposeReady = true;
                //        Engine._HandleProxiesPendingDisposal.Add((IntPtr)_HandleProxy);
                //    }
                //}
            }
            return false;
        }

        /// <summary>
        /// Completes the disposal of the native handle.
        /// <para>Note: A disposed native handle is simply cached for reuse, and always points back to the same managed handle.</para>
        /// </summary>
        internal void _CompleteDisposal()
        {
            if (!IsDisposed)
            {
                _HandleProxy->ManagedReferenceCount = 0;

                V8NetProxy.DisposeHandleProxy(_HandleProxy);

                _ManagedObjectID = -1;

                _HandleProxy = null;

                GC.RemoveMemoryPressure((Marshal.SizeOf(typeof(HandleProxy))));
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle is undefined or empty (empty is when this handle is an instance of 'Handle.Empty').
        /// <para>"Undefined" does not mean "null".  A variable (handle) can be defined and set to "null".</para>
        /// </summary>
        public bool IsUndefined { get { return IsEmpty || ValueType == JSValueType.Undefined; } }

        /// <summary>
        /// Returns true if this handle is empty (that is, equal to 'Handle.Empty'), and false if a valid handle exists.
        /// <para>An empty state is when a handle is set to 'Handle.Empty' and has no valid native V8 handle assigned.
        /// This is similar to "undefined"; however, this property will be true if a valid native V8 handle exists that is set to "undefined".</para>
        /// </summary>
        public bool IsEmpty { get { return _HandleProxy == null; } }

        public bool IsBoolean { get { return ValueType == JSValueType.Bool; } }
        public bool IsBooleanObject { get { return ValueType == JSValueType.BoolObject; } }
        public bool IsInt32 { get { return ValueType == JSValueType.Int32; } }
        public bool IsNumber { get { return ValueType == JSValueType.Number; } }
        public bool IsNumberObject { get { return ValueType == JSValueType.NumberObject; } }
        public bool IsString { get { return ValueType == JSValueType.String; } }
        public bool IsStringObject { get { return ValueType == JSValueType.StringObject; } }
        public bool IsObject { get { return ValueType == JSValueType.Object; } }
        public bool IsFunction { get { return ValueType == JSValueType.Function; } }
        public bool IsDate { get { return ValueType == JSValueType.Date; } }
        public bool IsArray { get { return ValueType == JSValueType.Array; } }
        public bool IsRegExp { get { return ValueType == JSValueType.RegExp; } }

        /// <summary>
        /// Returns true of the handle represents ANY object type.
        /// </summary>
        public bool IsObjectType
        {
            get
            {
                return ValueType == JSValueType.BoolObject
                    || ValueType == JSValueType.NumberObject
                    || ValueType == JSValueType.StringObject
                    || ValueType == JSValueType.Function
                    || ValueType == JSValueType.Date
                    || ValueType == JSValueType.RegExp
                    || ValueType == JSValueType.Array
                    || ValueType == JSValueType.Object;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the 'Value' property type cast to the expected type.
        /// Warning: No conversion is made between different value types.
        /// </summary>
        public DerivedType As<DerivedType>()
        {
            return _HandleProxy != null ? (DerivedType)Value : default(DerivedType);
        }

        /// Returns the 'LastValue' property type cast to the expected type.
        /// Warning: No conversion is made between different value types.
        public DerivedType LastAs<DerivedType>()
        {
            return _HandleProxy != null ? (DerivedType)LastValue : default(DerivedType);
        }

        /// <summary>
        /// Returns the underlying value converted if necessary to a Boolean type.
        /// </summary>
        public bool AsBoolean { get { return (bool)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to an Int32 type.
        /// </summary>
        public Int32 AsInt32 { get { return (Int32)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a double type.
        /// </summary>
        public double AsDouble { get { return (double)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a string type.
        /// </summary>
        public String AsString { get { return (String)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a DateTime type.
        /// </summary>
        public DateTime AsDate { get { return (DateTime)this; } }

        /// <summary>
        /// Returns this handle as a new JSProperty instance with default property attributes.
        /// </summary>
        public IJSProperty AsJSProperty() { return (JSProperty)this; }

        // --------------------------------------------------------------------------------------------------------------------

        public override string ToString()
        {
            if (IsUndefined) return "undefined";

            if (IsObjectType)
            {
                string managedType = "";

                if (HasManagedObject)
                {
                    var managedObjectInfo = Engine._Objects[ManagedObjectID];
                    var mo = managedObjectInfo._ManagedObject.Object;
                    managedType = " (" + (mo != null ? mo.GetType().Name : "associated managed object is null") + ")";
                }

                return "<object: " + Enum.GetName(typeof(JSValueType), ValueType) + managedType + ">";
            }

            var val = Value;

            return val != null ? val.ToString() : "null";
        }

        /// <summary>
        /// Checks if the wrapped handle reference is the same as the one compared with. This DOES NOT compare the underlying JavaScript values for equality.
        /// To test for JavaScript value equality, convert to a desired value-type instead by first casting as needed (i.e. (int)jsv1 == (int)jsv2).
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is InternalHandle && _HandleProxy == ((InternalHandle)obj)._HandleProxy;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle contains an error message (the string value is the message).
        /// If you have exception catching in place, you can simply call 'ThrowOnError()' instead.
        /// </summary>
        public bool IsError
        {
            get
            {
                return ValueType == JSValueType.InternalError
                    || ValueType == JSValueType.CompilerError
                    || ValueType == JSValueType.ExecutionError;
            }
        }

        /// <summary>
        /// Checks if the handle represents an error, and if so, throws one of the corresponding derived V8Exception exceptions.
        /// See 'JSValueType' for possible exception states.  You can check the 'IsError' property to see if this handle represents an error.
        /// <para>Exceptions thrown: V8InternalErrorException, V8CompilerErrorException, V8ExecutionErrorException, and V8Exception (for any general V8-related exceptions).</para>
        /// </summary>
        public void ThrowOnError()
        {
            if (IsError)
                switch (ValueType)
                {
                    case JSValueType.InternalError: throw new V8InternalErrorException(this);
                    case JSValueType.CompilerError: throw new V8CompilerErrorException(this);
                    case JSValueType.ExecutionError: throw new V8ExecutionErrorException(this);
                    default: throw new V8Exception(this); // (this will only happen if 'IsError' contains a type check that doesn't have any corresponding exception object)
                }
        }

        #endregion ### SHARED HANDLE CODE END ###
        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    /// <summary>
    /// Keeps track of native V8 handles (C++ native side).
    /// <para>DO NOT STORE THIS HANDLE. Use "Handle" instead (i.e. "Handle h = someInternalHandle;"), or use the value with the "using(someInternalHandle){}" statement.</para>
    /// </summary>
    public unsafe struct InternalHandle : IDisposable // ('IDisposable' will not box in a "using" statement: http://stackoverflow.com/questions/2412981/if-my-struct-implements-idisposable-will-it-be-boxed-when-used-in-a-using-statem)
    {
        // --------------------------------------------------------------------------------------------------------------------

        public static readonly InternalHandle Empty = new InternalHandle((HandleProxy*)null);

        // --------------------------------------------------------------------------------------------------------------------

        internal HandleProxy* _HandleProxy; // (the native proxy struct wrapped by this instance)
        internal bool _First; // (this is true if this is the FIRST handle to wrap the proxy [first handles may become automatically disposed internally if another handle is not created from it])

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Wraps a given native handle proxy to provide methods to operate on it.
        /// </summary>
        internal InternalHandle(HandleProxy* hp, bool checkIfFirst = true)
        {
            _HandleProxy = null;
            _First = false;
            Set(hp, checkIfFirst);
        }

        /// <summary>
        /// Sets this instance to the same specified handle value.
        /// </summary>
        public InternalHandle(InternalHandle handle)
        {
            _HandleProxy = null;
            _First = false;
            Set(handle);
        }

        /// <summary>
        /// Disposes of the current handle proxy reference (if not empty, and different) and replaces it with the specified new reference.
        /// <para>Note: This IS REQUIRED when setting handles, otherwise memory leaks may occur (the native V8 handles will never make it back into the cache).
        /// NEVER use the "=" operator to set a handle.  If using 'InternalHandle' handles, ALWAYS call "Dispose()" when they are no longer needed.
        /// To be safe, use the "using(SomeInternalHandle){}" statement (with 'InternalHandle' handles), or use "Handle refHandle = SomeInternalHandle;", to
        /// to convert it to a handle object that will dispose itself.</para>
        /// </summary>
        public InternalHandle Set(InternalHandle handle)
        {
            if (handle._First)
            {
                var h = Set(handle._HandleProxy);
                handle.Dispose(); // Disposes the handle if it is the first one (the first one is disposed automatically when passed back into the engine).
                return h;
            }
            else return Set(handle._HandleProxy);
        }

        internal InternalHandle Set(HandleProxy* hp, bool checkIfFirst = true)
        {
            if (_HandleProxy != hp)
            {
                if (_HandleProxy != null)
                    Dispose();

                _HandleProxy = hp;

                if (_HandleProxy != null)
                {
                    // ... verify the native handle proxy ID is within a valid range before storing it, and resize as needed ...

                    var engine = V8Engine._Engines[_HandleProxy->EngineID];
                    var handleID = _HandleProxy->ID;

                    if (handleID >= engine._HandleProxies.Length)
                    {
                        HandleProxy*[] handleProxies = new HandleProxy*[(100 + handleID) * 2];
                        for (var i = 0; i < engine._HandleProxies.Length; i++)
                            handleProxies[i] = engine._HandleProxies[i];
                        engine._HandleProxies = handleProxies;
                    }

                    engine._HandleProxies[handleID] = _HandleProxy;

                    if (checkIfFirst)
                        _First = (_HandleProxy->ManagedReferenceCount == 0);

                    _HandleProxy->ManagedReferenceCount++;

                    GC.AddMemoryPressure((Marshal.SizeOf(typeof(HandleProxy))));
                }
            }
            return this;
        }

        /// <summary>
        /// Handles should be set in either two ways: 1. by using the "Set()" method on the left side handle, or 2. using the "Clone()' method on the right side.
        /// Using the "=" operator to set a handle may cause memory leaks if not used correctly.
        /// See also: <seealso cref="Set()"/>
        /// </summary>
        public InternalHandle Clone()
        {
            return new InternalHandle().Set(this);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Internal handle values are a bit faster than having to create handle objects, but they MUST be explicitly disposed
        /// when no longer needed.  One rule of thumb is to always call "Set()" to set/update an internal value, and call
        /// "Dispose()" either within a class's finalization (destructor), or use it in a "using(){}" statement. Both of these
        /// will dispose of the handle in case exceptions occur. You may also use "try..finally", but that is not preferred
        /// best practice for V8.NET handles.
        /// </summary>
        public void Dispose()
        {
            if (_HandleProxy != null && _HandleProxy->ManagedReferenceCount > 0)
            {
                _HandleProxy->ManagedReferenceCount--;
                _First = false;

                if (_HandleProxy->ManagedReferenceCount == 0)
                    __TryDispose();
                else
                {
                    // (if this handle directly references a managed object, then notify the object info that it is weak if the ref count is 1)
                    if (_HandleProxy->_ManagedObjectID >= 0 && _HandleProxy->ManagedReferenceCount == 1)
                        Engine._Objects[_HandleProxy->_ManagedObjectID].Dispose();

                    _HandleProxy = null;
                }
            }
        }

        /// <summary>
        /// Disposes this handle if it is the first one (the first one is disposed automatically when passed back into the engine).
        /// </summary>
        internal void _DisposeIfFirst()
        {
            if (_First) Dispose();
        }

        /// <summary>
        /// Returns true if this handle is disposed (no longer in use).  Disposed handles are kept in a cache for performance reasons.
        /// </summary>
        public bool IsDisposed { get { return _HandleProxy == null || _HandleProxy->IsDisposed; } }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator InternalHandle(HandleProxy* hp)
        {
            return hp != null ? new InternalHandle(hp) : InternalHandle.Empty;
        }

        public static implicit operator Handle(InternalHandle handle)
        {
            return handle._HandleProxy != null ? new Handle(handle) : Handle.Empty;
        }

        public static implicit operator V8NativeObject(InternalHandle handle)
        {
            return handle.ManagedObject as V8NativeObject;
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static bool operator ==(InternalHandle h1, InternalHandle h2)
        {
            return h1._HandleProxy == h2._HandleProxy;
        }

        public static bool operator !=(InternalHandle h1, InternalHandle h2)
        {
            return !(h1 == h2);
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator HandleProxy*(InternalHandle handle)
        {
            return handle._HandleProxy;
        }

        // --------------------------------------------------------------------------------------------------------------------

        public static implicit operator bool(InternalHandle handle)
        {
            return (bool)Types.ChangeType(handle.Value, typeof(bool));
        }

        public static implicit operator Int32(InternalHandle handle)
        {
            return (Int32)Types.ChangeType(handle.Value, typeof(Int32));
        }

        public static implicit operator double(InternalHandle handle)
        {
            return (double)Types.ChangeType(handle.Value, typeof(double));
        }

        public static implicit operator string(InternalHandle handle)
        {
            var val = handle.Value;
            if (val == null) return "";
            return (string)Types.ChangeType(val, typeof(string));
        }

        public static implicit operator DateTime(InternalHandle handle)
        {
            var ms = (double)Types.ChangeType(handle.Value, typeof(double));
            return new DateTime(1970, 1, 1) + TimeSpan.FromMilliseconds(ms);
        }

        public static implicit operator JSProperty(InternalHandle handle)
        {
            return new JSProperty(handle);
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// This is used internally to pass on the internal handle values to internally called methods.
        /// This is required because the last method in a call chain is responsible to dispose first-time handles.
        /// (first-time handles are handles created internally immediately after native proxy calls)
        /// </summary>
        public InternalHandle PassOn()
        {
            InternalHandle h = this;
            _First = false; // ("first" is only valid if the handle is not passed on to another method)
            return h;
        }

        // --------------------------------------------------------------------------------------------------------------------
        #region ### SHARED HANDLE CODE START ###

        /// <summary>
        /// The ID (index) of this handle on both the native and managed sides.
        /// </summary>
        public int ID { get { return _HandleProxy != null ? _HandleProxy->ID : -1; } }

        /// <summary>
        /// The JavaScript type this handle represents.
        /// </summary>
        public JSValueType ValueType { get { return _HandleProxy != null ? _HandleProxy->_ValueType : JSValueType.Undefined; } }

        /// <summary>
        /// Used internally to determine the number of references to a handle.
        /// </summary>
        public Int64 ReferenceCount { get { return _HandleProxy != null ? _HandleProxy->ManagedReferenceCount : 0; } }

        /// <summary>
        /// A reference to the V8Engine instance that owns this handle.
        /// </summary>
        public V8Engine Engine { get { return _HandleProxy != null ? V8Engine._Engines[_HandleProxy->EngineID] : null; } }

        // --------------------------------------------------------------------------------------------------------------------
        // Managed Object Properties and References

        /// <summary>
        /// The ID of the managed object represented by this handle.
        /// This ID is expected when handles are passed to 'V8ManagedObject.GetObject()'.
        /// If this value is less than 0 (usually -1), then there is no associated managed object.
        /// </summary>
        public Int32 ManagedObjectID
        {
            get { return _HandleProxy == null ? -1 : _HandleProxy->_ManagedObjectID >= 0 ? _HandleProxy->_ManagedObjectID : V8NetProxy.GetHandleManagedObjectID(_HandleProxy); }
            internal set { if (_HandleProxy != null) _HandleProxy->_ManagedObjectID = value; }
        }

        /// <summary>
        /// Returns the managed object ID "as is".
        /// </summary>
        internal Int32 _ManagedObjectID
        {
            get { return _HandleProxy != null ? _HandleProxy->_ManagedObjectID : -1; }
            set { if (_HandleProxy != null) _HandleProxy->_ManagedObjectID = value; }
        }

        /// <summary>
        /// A reference to the managed object associated with this handle. This property is only valid for object handles, and will return null otherwise.
        /// Upon reading this property, if the managed object has been garbage collected (because no more handles or references exist), then a new basic 'V8NativeObject' instance will be created.
        /// <para>Instead of checking for 'null' (which may not work as expected), query 'HasManagedObject' instead.</para>
        /// </summary>
        public IV8NativeObject ManagedObject
        {
            get
            {
                if (HasManagedObject)
                {
                    return Engine._Objects[_HandleProxy->_ManagedObjectID].ManagedObject;
                }
                else return null;
            }
        }

        /// <summary>
        /// Returns true if this handle is associated with a managed object.
        /// <para>Note: This can be false even though 'IsObjectType' may be true.
        /// A handle can represent a native V8 object handle without requiring an associated managed object.</para>
        /// </summary>
        public bool HasManagedObject
        {
            get
            {
                if (IsObjectType && ManagedObjectID >= 0)
                {
                    var managedObjectInfo = Engine._Objects[_ManagedObjectID];
                    return (managedObjectInfo != null && managedObjectInfo._ManagedObject != null && managedObjectInfo._ManagedObject.Object != null);
                }
                else return false;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Reading from this property causes a native call to fetch the current V8 value associated with this handle.
        /// </summary>
        public object Value { get { if (_HandleProxy != null) { V8NetProxy.UpdateHandleValue(_HandleProxy); return _HandleProxy->Value; } else return null; } }

        /// <summary>
        /// Once "Value" is accessed to retrieve the JavaScript value in real time, there's no need to keep accessing it.  Just call this property
        /// instead (a small bit faster). Note: If the value changes again within the engine (i.e. another scripts executes), you may need to call
        /// 'Value' again to make sure any changes are reflected.
        /// </summary>
        public object LastValue
        {
            get
            {
                return _HandleProxy == null ? null : ((int)_HandleProxy->_ValueType) >= 0 ? _HandleProxy->Value : Value;
            }
        }

        /// <summary>
        /// Returns the array length for handles that represent arrays. For all other types, this returns 0.
        /// </summary>
        public Int32 ArrayLength { get { return IsArray ? V8NetProxy.GetArrayLength(_HandleProxy) : 0; } }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle is associated with a managed object that has no other references and is ready to be disposed.
        /// </summary>
        internal bool _IsWeakManagedObject
        {
            get
            {
                var id = _ManagedObjectID;
                if (id >= 0)
                {
                    var engine = Engine;
                    lock (engine._Objects)
                    {
                        var objInfo = engine._Objects[_ManagedObjectID];
                        if (objInfo != null)
                            return objInfo.IsManagedObjectWeak;
                        else
                            _ManagedObjectID = id = -1; // (this ID is no longer valid)
                    }
                }
                return id == -1;
            }
        }

        /// <summary>
        /// Returns true if the handle is weak and ready to be disposed.
        /// </summary>
        internal bool _IsWeakHandle { get { return _HandleProxy != null && _HandleProxy->ManagedReferenceCount <= 1; } }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// True if this handle is ready to be disposed (cached) on the native side.
        /// </summary>
        internal bool _IsInPendingDisposalQueue { get { return _HandleProxy != null && _HandleProxy->IsDisposeReady; } }

        /// <summary>
        /// Returns true if this handle is weak AND is associated with a weak managed object reference.
        /// When a handle is ready to be disposed, then calling "Dispose()" will succeed and cause the handle to be placed back into the cache on the native side.
        /// </summary>
        internal bool _IsDisposeReady { get { return _IsWeakHandle && _IsWeakManagedObject; } }

        /// <summary>
        /// Attempts to dispose of this handle (add it back into the native proxy cache for reuse).  If the handle represents a managed object,
        /// the dispose request is placed into a "pending disposal" queue. When the associated managed object
        /// no longer has any references, this method will be .
        /// <para>*** NOTE: This is called by Dispose() when the reference count becomes zero and should not be called directly. ***</para>
        /// </summary>
        internal bool __TryDispose()
        {
            if (_IsDisposeReady)
            {
                _HandleProxy->IsDisposeReady = true;
                _CompleteDisposal(); // (no need to wait! there's no managed object.)
                return true;

                //??var hasManagedObject = _ManagedObjectID >= 0;

                //??if (!hasManagedObject || _IsWeakManagedObject)
                //{
                //    _CompleteDisposal(); // (no need to wait! there's no managed object.)
                //    return true;
                //}
                //else if (hasManagedObject) // (managed side not yet ready)
                //{
                //    lock (Engine._HandleProxiesPendingDisposal)
                //    {
                //        _HandleProxy->IsDisposeReady = true;
                //        Engine._HandleProxiesPendingDisposal.Add((IntPtr)_HandleProxy);
                //    }
                //}
            }
            return false;
        }

        /// <summary>
        /// Completes the disposal of the native handle.
        /// <para>Note: A disposed native handle is simply cached for reuse, and always points back to the same managed handle.</para>
        /// </summary>
        internal void _CompleteDisposal()
        {
            if (!IsDisposed)
            {
                _HandleProxy->ManagedReferenceCount = 0;

                V8NetProxy.DisposeHandleProxy(_HandleProxy);

                _ManagedObjectID = -1;

                _HandleProxy = null;

                GC.RemoveMemoryPressure((Marshal.SizeOf(typeof(HandleProxy))));
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle is undefined or empty (empty is when this handle is an instance of 'Handle.Empty').
        /// <para>"Undefined" does not mean "null".  A variable (handle) can be defined and set to "null".</para>
        /// </summary>
        public bool IsUndefined { get { return IsEmpty || ValueType == JSValueType.Undefined; } }

        /// <summary>
        /// Returns true if this handle is empty (that is, equal to 'Handle.Empty'), and false if a valid handle exists.
        /// <para>An empty state is when a handle is set to 'Handle.Empty' and has no valid native V8 handle assigned.
        /// This is similar to "undefined"; however, this property will be true if a valid native V8 handle exists that is set to "undefined".</para>
        /// </summary>
        public bool IsEmpty { get { return _HandleProxy == null; } }

        public bool IsBoolean { get { return ValueType == JSValueType.Bool; } }
        public bool IsBooleanObject { get { return ValueType == JSValueType.BoolObject; } }
        public bool IsInt32 { get { return ValueType == JSValueType.Int32; } }
        public bool IsNumber { get { return ValueType == JSValueType.Number; } }
        public bool IsNumberObject { get { return ValueType == JSValueType.NumberObject; } }
        public bool IsString { get { return ValueType == JSValueType.String; } }
        public bool IsStringObject { get { return ValueType == JSValueType.StringObject; } }
        public bool IsObject { get { return ValueType == JSValueType.Object; } }
        public bool IsFunction { get { return ValueType == JSValueType.Function; } }
        public bool IsDate { get { return ValueType == JSValueType.Date; } }
        public bool IsArray { get { return ValueType == JSValueType.Array; } }
        public bool IsRegExp { get { return ValueType == JSValueType.RegExp; } }

        /// <summary>
        /// Returns true of the handle represents ANY object type.
        /// </summary>
        public bool IsObjectType
        {
            get
            {
                return ValueType == JSValueType.BoolObject
                    || ValueType == JSValueType.NumberObject
                    || ValueType == JSValueType.StringObject
                    || ValueType == JSValueType.Function
                    || ValueType == JSValueType.Date
                    || ValueType == JSValueType.RegExp
                    || ValueType == JSValueType.Array
                    || ValueType == JSValueType.Object;
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns the 'Value' property type cast to the expected type.
        /// Warning: No conversion is made between different value types.
        /// </summary>
        public DerivedType As<DerivedType>()
        {
            return _HandleProxy != null ? (DerivedType)Value : default(DerivedType);
        }

        /// Returns the 'LastValue' property type cast to the expected type.
        /// Warning: No conversion is made between different value types.
        public DerivedType LastAs<DerivedType>()
        {
            return _HandleProxy != null ? (DerivedType)LastValue : default(DerivedType);
        }

        /// <summary>
        /// Returns the underlying value converted if necessary to a Boolean type.
        /// </summary>
        public bool AsBoolean { get { return (bool)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to an Int32 type.
        /// </summary>
        public Int32 AsInt32 { get { return (Int32)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a double type.
        /// </summary>
        public double AsDouble { get { return (double)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a string type.
        /// </summary>
        public String AsString { get { return (String)this; } }

        /// <summary>
        /// Returns the underlying value converted if necessary to a DateTime type.
        /// </summary>
        public DateTime AsDate { get { return (DateTime)this; } }

        /// <summary>
        /// Returns this handle as a new JSProperty instance with default property attributes.
        /// </summary>
        public IJSProperty AsJSProperty() { return (JSProperty)this; }

        // --------------------------------------------------------------------------------------------------------------------

        public override string ToString()
        {
            if (IsUndefined) return "undefined";

            if (IsObjectType)
            {
                string managedType = "";

                if (HasManagedObject)
                {
                    var managedObjectInfo = Engine._Objects[ManagedObjectID];
                    var mo = managedObjectInfo._ManagedObject.Object;
                    managedType = " (" + (mo != null ? mo.GetType().Name : "associated managed object is null") + ")";
                }

                return "<object: " + Enum.GetName(typeof(JSValueType), ValueType) + managedType + ">";
            }

            var val = Value;

            return val != null ? val.ToString() : "null";
        }

        /// <summary>
        /// Checks if the wrapped handle reference is the same as the one compared with. This DOES NOT compare the underlying JavaScript values for equality.
        /// To test for JavaScript value equality, convert to a desired value-type instead by first casting as needed (i.e. (int)jsv1 == (int)jsv2).
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is InternalHandle && _HandleProxy == ((InternalHandle)obj)._HandleProxy;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this handle contains an error message (the string value is the message).
        /// If you have exception catching in place, you can simply call 'ThrowOnError()' instead.
        /// </summary>
        public bool IsError
        {
            get
            {
                return ValueType == JSValueType.InternalError
                    || ValueType == JSValueType.CompilerError
                    || ValueType == JSValueType.ExecutionError;
            }
        }

        /// <summary>
        /// Checks if the handle represents an error, and if so, throws one of the corresponding derived V8Exception exceptions.
        /// See 'JSValueType' for possible exception states.  You can check the 'IsError' property to see if this handle represents an error.
        /// <para>Exceptions thrown: V8InternalErrorException, V8CompilerErrorException, V8ExecutionErrorException, and V8Exception (for any general V8-related exceptions).</para>
        /// </summary>
        public void ThrowOnError()
        {
            if (IsError)
                switch (ValueType)
                {
                    case JSValueType.InternalError: throw new V8InternalErrorException(this);
                    case JSValueType.CompilerError: throw new V8CompilerErrorException(this);
                    case JSValueType.ExecutionError: throw new V8ExecutionErrorException(this);
                    default: throw new V8Exception(this); // (this will only happen if 'IsError' contains a type check that doesn't have any corresponding exception object)
                }
        }

        #endregion ### SHARED HANDLE CODE END ###
        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
