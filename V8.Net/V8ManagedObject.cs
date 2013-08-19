﻿using System;
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
    /// The 'V8ManagedObject' class implements 'DynamicObject' for you, but if dynamic objects are not required, feel free to implement
    /// the 'IV8ManagedObject' interface for your own classes instead.
    /// </summary>
    public interface IV8ManagedObject : IV8NativeObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object.
        /// This simply calls 'V8Engine.GetObjectHandle()'.
        /// </summary>
        new InternalHandle Handle { get; }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds a Key->Value reference to all property names and values for the JavaScript object that this managed object represents.
        /// </summary>
        IDictionary<string, IJSProperty> Properties { get; }

        /// <summary>
        /// A reference to the ObjectTemplate that created this object.
        /// <para>Note: This simply calls 'V8Engine.GetObjectTemplate()'.</para>
        /// </summary>
        ObjectTemplate ObjectTemplate { get; }

        /// <summary>
        /// Returns the value/object handle associated with the specified property.
        /// </summary>
        new IJSProperty this[string propertyName] { get; set; }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for retrieving a value.
        /// <para>To allow the V8 engine to perform the default get action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle NamedPropertyGetter(ref string propertyName);

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for setting values.
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
#if V2 || V3 || V3_5
        InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value, V8PropertyAttributes attributes);
#else
        InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.Undefined);
#endif

        /// <summary>
        /// Let's the V8 engine know the attributes for the specified property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        V8PropertyAttributes? NamedPropertyQuery(ref string propertyName);

        /// <summary>
        /// Intercepts JavaScript request to delete a property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        bool? NamedPropertyDeleter(ref string propertyName);

        /// <summary>
        /// Returns the results of enumeration (such as when "for..in" is used).
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle NamedPropertyEnumerator();

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for retrieving a value.
        /// <para>To allow the V8 engine to perform the default get action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertyGetter(int index);

        /// <summary>
        /// Intercepts JavaScript access for properties on the associated JavaScript object for setting values.
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertySetter(int index, InternalHandle value);

        /// <summary>
        /// Let's the V8 engine know the attributes for the specified property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        V8PropertyAttributes? IndexedPropertyQuery(int index);

        /// <summary>
        /// Intercepts JavaScript request to delete a property.
        /// <para>To allow the V8 engine to perform the default get action, return "null".</para>
        /// </summary>
        bool? IndexedPropertyDeleter(int index);

        /// <summary>
        /// Returns the results of enumeration (such as when "for..in" is used).
        /// <para>To allow the V8 engine to perform the default set action, return "Handle.Empty".</para>
        /// </summary>
        InternalHandle IndexedPropertyEnumerator();

        // --------------------------------------------------------------------------------------------------------------------
    }

    /// <summary>
    /// Represents a C# (managed) JavaScript object.  Properties are set on the object within the class itself, and not within V8.
    /// This is done by using V8 object interceptors (callbacks).  By default, this object is used for the global environment.
    /// <para>The inherited 'V8NativeObject' base class implements 'DynamicObject' for you, but if dynamic objects are not required, 
    /// feel free to implement the 'IV8ManagedObject' interface for your own classes instead; however, you also will have to call the
    /// V8NetProxy static methods yourself if you need functionality supplied by V8NativeObject.</para>
    /// <para>Note: It's faster to work with the properties on the managed side using this object, but if a lot of properties won't be changing,
    /// it may be faster to access properties within V8 itself.  To do so, simply create a basic V8NativeObject using 'V8Engine.CreateObject()'
    /// instead.</para>
    /// </summary>
    public class V8ManagedObject : V8NativeObject, IV8ManagedObject
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A reference to the managed object handle that wraps the native V8 handle for this managed object
        /// (this simply calls 'base.Handle'). You cannot change handles on managed objects because they are usually associated with object interceptors.
        /// </summary>
        InternalHandle IV8ManagedObject.Handle { get { return base.Handle; } }

        /// <summary>
        /// A reference to the ObjectTemplate instance that owns this object.
        /// </summary>
        public ObjectTemplate ObjectTemplate { get { return (ObjectTemplate)Engine.GetObjectTemplate(this); } }

        /// <summary>
        /// Holds a Key->Value reference to all property names and values for the JavaScript object that this managed object represents.
        /// </summary>
        public virtual IDictionary<string, IJSProperty> Properties { get { return _Properties ?? (_Properties = new Dictionary<string, IJSProperty>()); } }
        IDictionary<string, IJSProperty> _Properties;

        IJSProperty IV8ManagedObject.this[string propertyName]
        {
            get
            {
                IJSProperty value;
                if (Properties.TryGetValue(propertyName, out value))
                    return value;
                return JSProperty.Empty;
            }
            set
            {
                Properties[propertyName] = value;
            }
        }

        public override InternalHandle this[string propertyName]
        {
            get
            {
                IJSProperty value;
                if (Properties.TryGetValue(propertyName, out value))
                    return value.Value;
                return InternalHandle.Empty;
            }
            set
            {
                NamedPropertySetter(ref propertyName, value);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called immediately after creating an object instance and setting the V8Engine property.
        /// </summary>
        public override void Initialize() { base.Initialize(); }

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle NamedPropertyGetter(ref string propertyName)
        {
            return this[propertyName];
        }

        public virtual InternalHandle NamedPropertySetter(ref string propertyName, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.Undefined)
        {
            var jsVal = ((IV8ManagedObject)this)[propertyName];

            if (jsVal.IsEmpty)
                ((IV8ManagedObject)this)[propertyName] = jsVal = new JSProperty(value, attributes != V8PropertyAttributes.Undefined ? attributes : V8PropertyAttributes.None);
            else
            {
                if (attributes != V8PropertyAttributes.Undefined)
                {
                    jsVal.Attributes = attributes;
                    jsVal.Value.Set(value); // (note: updating attributes automatically assumes writable access)
                }
                else if ((jsVal.Attributes & V8PropertyAttributes.ReadOnly) == 0)
                    jsVal.Value.Set(value);
            }

            return jsVal.Value;
        }

        public virtual V8PropertyAttributes? NamedPropertyQuery(ref string propertyName)
        {
            return ((IV8ManagedObject)this)[propertyName].Attributes;
        }

        public virtual bool? NamedPropertyDeleter(ref string propertyName)
        {
            var jsVal = ((IV8ManagedObject)this)[propertyName];
            if ((jsVal.Attributes & V8PropertyAttributes.DontDelete) != 0)
                return false;
            return Properties.Remove(propertyName);
        }

        public virtual InternalHandle NamedPropertyEnumerator()
        {
            List<string> names = new List<string>(Properties.Count);
            foreach (var prop in Properties)
                if (prop.Value != null && (prop.Value.Attributes & V8PropertyAttributes.DontEnum) == 0)
                    names.Add(prop.Key);
            return Engine.CreatArray(names);
        }

        // --------------------------------------------------------------------------------------------------------------------

        public virtual InternalHandle IndexedPropertyGetter(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyGetter(ref propertyName);
        }

        public virtual InternalHandle IndexedPropertySetter(int index, InternalHandle value)
        {
            var propertyName = index.ToString();
            return NamedPropertySetter(ref propertyName, value);
        }

        public virtual V8PropertyAttributes? IndexedPropertyQuery(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyQuery(ref propertyName);
        }

        public virtual bool? IndexedPropertyDeleter(int index)
        {
            var propertyName = index.ToString();
            return NamedPropertyDeleter(ref propertyName);
        }

        public virtual InternalHandle IndexedPropertyEnumerator()
        {
            return NamedPropertyEnumerator();
        }

        // --------------------------------------------------------------------------------------------------------------------
        // Dynamic Support for .NET v4.0 and higher.

#if V2 || V3 || V3_5
#else

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return _Properties.Keys;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var jsVal = ((IV8ManagedObject)this)[binder.Name];
            if (!jsVal.IsEmpty)
            {
                var val = jsVal.Value;
                if (val.HasManagedObject)
                    result = val.ManagedObject; // (for objects, return the instance instead of a value handle)
                else
                    result = jsVal.Value;
                return true;
            }
            result = null;
            return false;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            var propertyName = binder.Name;

            if (value is Handle || value is InternalHandle)
                NamedPropertySetter(ref propertyName, (InternalHandle)value); // (objects are detected and added directly! ;))
            else if (value is IV8NativeObject)
                NamedPropertySetter(ref propertyName, ((IV8NativeObject)value).Handle); // (objects are detected and added directly! ;))
            else if (value is IJSProperty)
                ((IV8ManagedObject)this)[propertyName] = (IJSProperty)value; // (IJSProperty values are detected and set directly! ;))
            else
                NamedPropertySetter(ref propertyName, Engine.CreateNativeValue(value));

            return true;
        }

        public override bool TryDeleteMember(DeleteMemberBinder binder)
        {
            return _Properties.Remove(binder.Name);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            if (indexes.Length == 1)
            {
                var o = indexes[0];
                if (o != null)
                {
                    var jsVal = ((IV8ManagedObject)this)[o.ToString()];
                    if (!jsVal.IsEmpty) { result = jsVal.Value; return true; }
                }
            }
            result = null;
            return false;
        }

#endif

        // --------------------------------------------------------------------------------------------------------------------
        // Since some base methods operate on object properties, and the properties exist on this managed object, we override
        // them here to speed things up.

#if V2 || V3 || V3_5
        public override bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes)
#else
        public override bool SetProperty(string name, InternalHandle value, V8PropertyAttributes attributes = V8PropertyAttributes.None)
#endif
        {
            NamedPropertySetter(ref name, value, attributes);
            return true;
        }

        public override bool SetProperty(int index, InternalHandle value)
        {
            IndexedPropertySetter(index, value);
            return true;
        }

        public override InternalHandle GetProperty(string name)
        {
            return NamedPropertyGetter(ref name);
        }

        public override InternalHandle GetProperty(int index)
        {
            return IndexedPropertyGetter(index);
        }

        public override bool DeleteProperty(string name)
        {
            return NamedPropertyDeleter(ref name) ?? false;
        }

        public override bool DeleteProperty(int index)
        {
            return IndexedPropertyDeleter(index) ?? false;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    /// <summary>
    /// This interface has been renamed to 'IV8ManagedObject'.
    /// This is to bring to the attention that 'IV8ManagedObject' type objects are the true native V8 objects and are many times faster.
    /// </summary>
    public interface IV8Object { }

    /// <summary>
    /// This class has been renamed to 'V8ManagedObject'.
    /// This is to bring to the attention that 'V8ManagedObject' type objects are the true native V8 objects and are many times faster.
    /// </summary>
    public class V8Object { }

    // ========================================================================================================================
}
