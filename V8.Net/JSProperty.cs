﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace V8.Net
{
    // ========================================================================================================================

    public interface IJSProperty
    {
        /// <summary>
        /// A JavaScript associated value.
        /// Call one of the "Create???()" methods to create/build a required type for the JavaScript value that represents 'Source'.
        /// </summary>
        InternalHandle Value { get; set; }

        /// <summary>
        /// 'V8PropertyAttributes' flags combined to describe the value, such as visibility, or what kind of access is allowed.
        /// </summary>
        V8PropertyAttributes Attributes { get; set; }

        /// <summary>
        /// Returns true if the instance represents a non-existent value.
        /// </summary>
        bool IsEmpty { get; }
    }

    /// <summary>
    /// A convenient JavaScript property wrapper which also holds JavaScript property attribute flags. The generic type 'TSourceValue' is the type of value to be stored on the managed side.
    /// This JSProperty object also allows storing an associated V8 value handle. Having both a managed source value and a separate V8 value allows the source
    /// value to be represented in JavaScript as a different type. For example, a value may exist locally as a string, but in JavaScript as a number (or vice versa).
    /// Developers can inherit from this class if desired, or choose to go with a custom implementation using the IJSProperty interface instead.
    /// </summary>
    /// <typeparam name="TValueSource">When implementing properties for an IV8ManagedObject, this is the type that will store the property source value/details (such as 'object' - as already implemented in the derived 'JSProperty' class [the non-generic version]).</typeparam>
    public class JSProperty<TValueSource> : IJSProperty
    {
        /// <summary>
        /// This is a developer-defined source reference for the JavaScript 'Value' property if needed. It is not used by V8.Net.
        /// </summary>
        public TValueSource Source;

        /// <summary>
        /// A JavaScript associated value.  By default, this returns 'Handle.Empty' (which means 'Value' is 'null' internally).
        /// Call one of the "V8Engine.Create???()" methods to create/build a required type for the JavaScript value that represents 'Source'.
        /// </summary>
        InternalHandle IJSProperty.Value { get { return _Value; } set { _Value.Set(value); } }
        InternalHandle _Value;

        /// <summary>
        /// 'V8PropertyAttributes' flags combined to describe the value, such as visibility, or what kind of access is allowed.
        /// </summary>
        V8PropertyAttributes IJSProperty.Attributes { get { return _Attributes; } set { _Attributes = value; } }
        V8PropertyAttributes _Attributes;

        /// <summary>
        /// Returns true if this instance represents a NULL INSTANCE (such as JSProperty.Null).  If this is true, then setting the Value or Attribute properties will cause an error.
        /// A "NULL" instance means nothing EXISTS. To set a null VALUE, you must replace a NULL instance with "new JSProperty()".
        /// </summary>
        bool IJSProperty.IsEmpty { get { return false; } }

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty(V8PropertyAttributes attributes = V8PropertyAttributes.None) { Source = default(TValueSource); _Attributes = attributes; }
        public JSProperty(TValueSource source, V8PropertyAttributes attributes = V8PropertyAttributes.None) { Source = source; _Attributes = attributes; }
        public JSProperty(TValueSource source, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None) : this(source, attributes) { _Value.Set(value); }
        public JSProperty(InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None) : this(default(TValueSource), value, attributes) { }
        public JSProperty(V8Engine engine, object value, V8PropertyAttributes attributes = V8PropertyAttributes.None)
            : this(InternalHandle.Empty, attributes)
        {
            _Value.Set(engine != null ? engine.CreateValue(value) : InternalHandle.Empty);
        }

        ~JSProperty() { _Value.Dispose(); }

        public static implicit operator InternalHandle(JSProperty<TValueSource> jsVal)
        { return jsVal._Value; }

        public override string ToString()
        {
            return _Value.ToString();
        }
    }

    /// <summary>
    /// A convenient 'MemberInfo' specific wrapper which holds JavaScript property value and attribute flags for managed object members.
    /// For custom implementations, see <see cref="JSProperty&lt;TValueSource>"/>.
    /// </summary>
    public class JSProperty : JSProperty<object>
    {
        class _Empty : IJSProperty
        {
            InternalHandle IJSProperty.Value { get { return InternalHandle.Empty; } set { throw new InvalidOperationException("This JSProperty instance represents a NULL state and cannot be set."); } }
            V8PropertyAttributes IJSProperty.Attributes { get { return V8PropertyAttributes.None; } set { throw new InvalidOperationException("This JSProperty instance represents a NULL state and cannot have attributes."); } }
            bool IJSProperty.IsEmpty { get { return true; } }
        }

        /// <summary>
        /// Represents an empty JSProperty, which is simply used to return an empty 'Value' property (as 'Handle.Empty').
        /// <para>The purpose is to prevent having to perform null reference checks when needing to reference the 'Value' property.</para>
        /// </summary>
        public readonly static IJSProperty Empty = new _Empty();

        /// <summary>
        /// Create a new JSProperty instance to help keep track of JavaScript object properties on managed objects.
        /// </summary>
        public JSProperty(V8PropertyAttributes attributes = V8PropertyAttributes.None) : base(attributes) { }
        public JSProperty(object source, V8PropertyAttributes attributes = V8PropertyAttributes.None) : base(source, attributes) { }
        public JSProperty(object source, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None) : base(source, value, attributes) { }
        public JSProperty(InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None) : base(value, attributes) { }
        public JSProperty(V8Engine engine, object value, V8PropertyAttributes attributes = V8PropertyAttributes.None) : base(engine, value, attributes) { }

    }

    // ========================================================================================================================
}
