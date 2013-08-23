using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

#if !(V1_1 || V2 || V3 || V3_5)
using System.Dynamic;
#endif

namespace V8.Net
{
    // ========================================================================================================================

    /// <summary>
    /// A V8.NET binder for CLR types.
    /// </summary>
    public sealed class TypeBinder
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Holds a list of all binders that can operate on an instance of a given type.
        /// </summary>
        static readonly Dictionary<Type, TypeBinder> _Binders = new Dictionary<Type, TypeBinder>();

        /// <summary>
        /// Returns true if a binding exists for the specified type.
        /// </summary>
        public static bool BindingExists(Type type) { return _Binders.ContainsKey(type); }

        /// <summary>
        /// Registers a binding for the given type.  If a type already exists that doesn't match the given parameters, it is replaced.
        /// <para>This is done implicitly, so there's no need to register types before binding them; however, explicitly registering a type using this
        /// method gives the user more control over the behaviour of the binding process.</para>
        /// </summary>
        /// <param name="type">The type to create and cache a binding for.</param>
        /// <param name="recursive">When an object is bound, only the object instance itself is bound (and not any reference members). If true, then nested object references are included.</param>
        /// <param name="defaultMemberAttributes">Default member attributes for members that don't have the 'ScriptMember' attribute.</param>
        public static TypeBinder RegisterType(Type type, bool recursive = false, V8PropertyAttributes defaultMemberAttributes = V8PropertyAttributes.Undefined)
        {
            TypeBinder binder = null;
            lock (_Binders) { _Binders.TryGetValue(type, out binder); }
            if (binder != null && binder._Recursive == recursive && binder._DefaultMemberAttributes == defaultMemberAttributes)
                return binder;
            else
                return new TypeBinder(type, recursive, defaultMemberAttributes);
        }

        /// <summary>
        /// Registers a binding for the given type.  If a type already exists that doesn't match the given parameters, it is replaced.
        /// <para>This is done implicitly, so there's no need to register types before binding them; however, explicitly registering a type using this
        /// method gives the user more control over the behaviour of the binding process.</para>
        /// </summary>
        /// <typeparam name="T">The type to create and cache a binding for.</typeparam>
        /// <param name="recursive">When an object is bound, only the object instance itself is bound (and not any reference members). If true, then nested object references are included.</param>
        /// <param name="defaultMemberAttributes">Default member attributes for members that don't have the 'ScriptMember' attribute.</param>
        public static TypeBinder RegisterType<T>(bool recursive = false, V8PropertyAttributes defaultMemberAttributes = V8PropertyAttributes.Undefined)
        { return RegisterType(typeof(T), recursive, defaultMemberAttributes); }

        // --------------------------------------------------------------------------------------------------------------------

        public Type BoundType { get; private set; } // (type of object this binding represents)

        //??readonly Dictionary<string, V8NativeObjectPropertyGetter> _GetAccessors = new Dictionary<string, V8NativeObjectPropertyGetter>(); // (need to make sure the "thunks" are not GC'd!)
        //??readonly Dictionary<string, V8NativeObjectPropertySetter> _SetAccessors = new Dictionary<string, V8NativeObjectPropertySetter>(); // (need to make sure the "thunks" are not GC'd!)

        /// <summary>
        /// 
        /// </summary>
        readonly Dictionary<MemberInfo, V8PropertyAttributes> _Attributes = new Dictionary<MemberInfo, V8PropertyAttributes>();

        ///// <summary>
        ///// A type binder is applied to a handle using "accessors", which will call back into the type binder.
        ///// </summary>
        //internal object _Object;

        internal bool _Recursive;

        internal V8PropertyAttributes _DefaultMemberAttributes;

        /// <summary>
        /// The methods found that will be bound to new object instances.
        /// </summary>
        readonly Dictionary<string, List<MethodInfo>> _Methods = new Dictionary<string, List<MethodInfo>>();

        /// <summary>
        /// The properties found that will be bound to new object instances.
        /// </summary>
        readonly Dictionary<string, PropertyInfo> _Propeties = new Dictionary<string, PropertyInfo>();

        /// <summary>
        /// The fields found that will be bound to new object instances.
        /// </summary>
        readonly Dictionary<string, FieldInfo> _Fields = new Dictionary<string, FieldInfo>();

        // --------------------------------------------------------------------------------------------------------------------

        internal TypeBinder(Type type, bool recursive = false, V8PropertyAttributes defaultMemberAttributes = V8PropertyAttributes.Undefined)
        {
            BoundType = type;
            _Recursive = recursive;
            _DefaultMemberAttributes = defaultMemberAttributes;

            _BindType();

            lock (_Binders) { _Binders[type] = this; }
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _BindType()
        {
            if (BoundType == null) throw new InvalidOperationException("'BoundType' is null.");

            var members = BoundType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            var scriptObjectAttribute = (from a in BoundType.GetCustomAttributes(true) where a is ScriptObject select (ScriptObject)a).FirstOrDefault();

            string objectScriptName = scriptObjectAttribute != null && !string.IsNullOrEmpty(scriptObjectAttribute.TypeName) ? scriptObjectAttribute.TypeName : BoundType.Name;

            var defaultAttribute = _DefaultMemberAttributes != V8PropertyAttributes.Undefined ? (ScriptMemberSecurity)_DefaultMemberAttributes
                : scriptObjectAttribute != null ? scriptObjectAttribute.Security : ScriptMemberSecurity.ReadWrite;

            object[] attributes;
            int mi, ai;
            string memberName;
            ScriptMemberSecurity attribute;
            ScriptMember scriptMemberAttrib;

            for (mi = 0; mi < members.Length; mi++)
            {
                var member = members[mi]; // (need to use 'var' for the lambda closures in 'SetAccessor()' below)

                if (member.IsDefined(typeof(NoScriptAccess), true)) continue; // (don't allow access, skip)

                memberName = member.Name;
                attribute = defaultAttribute;

                attributes = member.GetCustomAttributes(true);

                for (ai = 0; ai < attributes.Length; ai++)
                {
                    scriptMemberAttrib = attributes[ai] as ScriptMember;
                    if (scriptMemberAttrib != null)
                    {
                        if (!string.IsNullOrEmpty(scriptMemberAttrib.InScriptName))
                            memberName = scriptMemberAttrib.InScriptName;

                        if (scriptMemberAttrib.Security != (ScriptMemberSecurity)V8PropertyAttributes.Undefined)
                            attribute = scriptMemberAttrib.Security;
                    }
                }

                attribute |= (ScriptMemberSecurity)V8PropertyAttributes.DontDelete;

                _AddMember(memberName, (V8PropertyAttributes)attribute, member);
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        internal void _AddMember(string memberName, V8PropertyAttributes attribute, MemberInfo memberInfo)
        {
            if (memberInfo.MemberType == MemberTypes.Field)
            {
                var fieldInfo = memberInfo as FieldInfo;

                if (!_Recursive && fieldInfo.FieldType.IsClass && fieldInfo.FieldType != typeof(string)) return; // (don't include nested objects, except strings)

                if (fieldInfo.IsInitOnly)
                    attribute |= V8PropertyAttributes.ReadOnly;

                _Fields[memberName] = fieldInfo;
                _Attributes[fieldInfo] = attribute;
            }
            else if (memberInfo.MemberType == MemberTypes.Property)
            {
                var propertyInfo = memberInfo as PropertyInfo;

                if (!_Recursive && propertyInfo.PropertyType.IsClass && propertyInfo.PropertyType != typeof(string)) return; // (don't include nested objects, except strings)

                if (!propertyInfo.CanWrite)
                    attribute |= V8PropertyAttributes.ReadOnly;

                _Propeties[memberName] = propertyInfo;
                _Attributes[propertyInfo] = attribute;
            }
            else if (memberInfo.MemberType == MemberTypes.Method)
            {
                var methodInfo = memberInfo as MethodInfo;
                if (!methodInfo.IsSpecialName)
                {
                    List<MethodInfo> methodInfoList;

                    if (!_Methods.TryGetValue(memberName, out methodInfoList))
                        _Methods[memberName] = methodInfoList = new List<MethodInfo>();

                    methodInfoList.Add(methodInfo);
                    _Attributes[methodInfo] = attribute;
                }
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Binds the specified instance it to this binder, and returns the new instance.
        /// The object MUST be of the same type as represented by this binder.
        /// </summary>
        internal ObjectBinder _ApplyBinding(ObjectBinder obj)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            if (obj.ObjectType == null) throw new InvalidOperationException("'obj.ObjectType' cannot be null.");

            if (obj.ObjectType != BoundType)
                throw new InvalidOperationException("Cannot bind the object instance of type '" + obj.ObjectType.Name + "' with bindings for type '" + BoundType.Name + "'.");

            foreach (var kv in _Fields)
                _Bind(obj, kv.Key, _Attributes[kv.Value], kv.Value);

            foreach (var kv in _Propeties)
                _Bind(obj, kv.Key, _Attributes[kv.Value], kv.Value);

            string memberName;

            foreach (var kv in _Methods)
            {
                memberName = kv.Key;

                // ... for methods, the overloads must be considered ...

                foreach (var methodInfo in kv.Value)
                {

                    if (kv.Value.Count > 1)
                    {
                        // ... need to append to the member name, as there are multiple overloads ...
                        foreach (var param in methodInfo.GetParameters())
                        {
                            memberName += "_" + param.ParameterType.Name;
                        }
                    }

                    _Bind(obj, memberName, _Attributes[methodInfo], methodInfo);
                }
            }

            return obj;
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _Bind(ObjectBinder obj, string memberName, V8PropertyAttributes attributes, FieldInfo fieldInfo)
        {
            V8NativeObjectPropertyGetter getter;
            V8NativeObjectPropertySetter setter;

            if (obj.Engine.Bind(obj, memberName, out getter, out setter, fieldInfo) == null)
            {
                obj.SetAccessor(memberName, getter, setter, attributes); // TODO: Investigate need to add access control value.
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _Bind(ObjectBinder obj, string memberName, V8PropertyAttributes attributes, PropertyInfo propInfo)
        {
            V8NativeObjectPropertyGetter getter;
            V8NativeObjectPropertySetter setter;

            if (obj.Engine.Bind(obj, memberName, out getter, out setter, propInfo) == null)
            {
                obj.SetAccessor(memberName, getter, setter, attributes); // TODO: Investigate need to add access control value.
            }
        }

        // --------------------------------------------------------------------------------------------------------------------

        void _Bind(ObjectBinder obj, string memberName, V8PropertyAttributes attributes, MethodInfo methodInfo)
        {
            V8Function func;
            if (obj.Engine.Bind(obj, memberName, out func, methodInfo) == null)
            {
                obj.SetProperty(memberName, func, attributes); // TODO: Investigate need to add access control value.
            }
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================

    public class ObjectBinder : V8NativeObject
    {
        public object Object
        {
            get { return _Object; }
            set
            {
                if (value == null) throw new InvalidOperationException("'value' cannot be null.");

                var valueType = value.GetType();

                if (_ObjectType == null)
                {
                    _Object = value;
                    ObjectType = valueType;
                }
                else if (valueType == _ObjectType)
                    _Object = value;
                else
                    throw new InvalidOperationException("Once an object is set, you can only replace the instance with another of the SAME type.");
            }
        }
        internal object _Object;

        public Type ObjectType
        {
            get { return _ObjectType; }
            private set
            {
                if (value == null) throw new InvalidOperationException("'value' cannot be null.");
                if (_ObjectType == null)
                {
                    _ObjectType = value;
                    TypeBinder = TypeBinder.RegisterType(_ObjectType);
                    TypeBinder._ApplyBinding(this);
                }
            }
        }
        Type _ObjectType;

        public TypeBinder TypeBinder { get; private set; }

        public override void Initialize()
        {
            base.Initialize();

            if (ObjectType == null && _Object != null)
                ObjectType = _Object.GetType();
        }
    }

    public class ObjectBinder<T> : ObjectBinder where T : class, new()
    {
        public ObjectBinder() { Object = new T(); }
    }

    // ========================================================================================================================
    // The binding section has methods to help support exposing objects and types to the V8 JavaScript environment.

    public unsafe partial class V8Engine
    {
        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Binds a getter and setter to read and/or write to the specified data member.
        /// </summary>
        /// <param name="obj">An 'ObjectBinder' instance to bind the accessor callbacks to.</param>
        /// <param name="memberName">The name of a member on '{ObjectBinder}.Object', or a new in-script name if 'fieldInfo' is supplied.</param>
        /// <param name="getter">Returns the getter delegate to use for a native callback.</param>
        /// <param name="setter">Returns the setter delegate to use for a native callback.</param>
        /// <param name="fieldInfo">If null, this will be pulled using 'memberName'.  If specified, then 'memberName' can be used to rename the field name.</param>
        /// <returns>An exception on error, or null on success.</returns>
        public Exception Bind(ObjectBinder obj, string memberName,
            out V8NativeObjectPropertyGetter getter, out V8NativeObjectPropertySetter setter,
            FieldInfo fieldInfo = null)
        {
            getter = null;
            setter = null;

            if (obj == null) return new ArgumentNullException("'obj' is null.");

            if (fieldInfo == null)
            {
                if (obj.ObjectType == null) return new InvalidOperationException("'obj.ObjectType' is null.");

                fieldInfo = obj.ObjectType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (fieldInfo == null)
                    return new ArgumentNullException("'fieldInfo' cannot be determined - the object field '" + memberName + "' cannot be found/accessed.");
            }

            if (string.IsNullOrEmpty(memberName))
                memberName = fieldInfo.Name;

            if (fieldInfo.FieldType == typeof(bool))
                getter = CreateGetAccessor<bool>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(byte))
                getter = CreateGetAccessor<byte>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(sbyte))
                getter = CreateGetAccessor<sbyte>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(Int16))
                getter = CreateGetAccessor<Int16>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(UInt16))
                getter = CreateGetAccessor<UInt16>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(Int32))
                getter = CreateGetAccessor<Int32>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(UInt32))
                getter = CreateGetAccessor<UInt32>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(Int64))
                getter = CreateGetAccessor<Int64>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(UInt64))
                getter = CreateGetAccessor<UInt64>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(Single))
                getter = CreateGetAccessor<Single>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(float))
                getter = CreateGetAccessor<float>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(double))
                getter = CreateGetAccessor<double>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(string))
                getter = CreateGetAccessor<string>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(char))
                getter = CreateGetAccessor<char>(obj, fieldInfo);

            else if (fieldInfo.FieldType == typeof(DateTime))
                getter = CreateGetAccessor<DateTime>(obj, fieldInfo);
            else if (fieldInfo.FieldType == typeof(TimeSpan))
                getter = CreateGetAccessor<TimeSpan>(obj, fieldInfo);

            else
                return new NotSupportedException("Member '" + memberName + "' is of an unsupported type '" + fieldInfo.FieldType.Name + "'.");

            setter = CreateSetAccessor(obj, fieldInfo);

            return null;

        }

        public V8NativeObjectPropertySetter CreateSetAccessor(ObjectBinder obj, FieldInfo fieldInfo)
        {
            if (obj == null) throw new InvalidOperationException("'obj' is null.");

            return (InternalHandle _this, string propertyName, InternalHandle value) => { fieldInfo.SetValue(obj.Object, Types.ChangeType(value.Value, fieldInfo.FieldType)); return InternalHandle.Empty; };
        }

        public V8NativeObjectPropertyGetter CreateGetAccessor<T>(ObjectBinder obj, FieldInfo fieldInfo)
        {
            if (obj == null) throw new InvalidOperationException("'obj' is null.");

            var isSystemType = obj.ObjectType.Namespace == "System";

            if (isSystemType)
                return (InternalHandle _this, string propertyName) =>
                {
                    // (note: it's an error to read some properties in special cases on certain system type instances (such as 'Type.GenericParameterPosition'), so just ignore and return the message on system instances)
                    try { return CreateValue((T)fieldInfo.GetValue(obj.Object)); }
                    catch (Exception ex) { return CreateValue(Exceptions.GetFullErrorMessage(ex)); }
                };
            else
                return (InternalHandle _this, string propertyName) =>
                {
                    return CreateValue((T)fieldInfo.GetValue(obj.Object));
                };
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Binds a getter and setter to read and/or write to the specified data member.
        /// </summary>
        /// <param name="obj">An 'ObjectBinder' instance to bind the accessor callbacks to.</param>
        /// <param name="memberName">The name of a member on '{ObjectBinder}.Object', or a new in-script name if 'propInfo' is supplied.</param>
        /// <param name="getter">Returns the getter delegate to use for a native callback.</param>
        /// <param name="setter">Returns the setter delegate to use for a native callback.</param>
        /// <param name="propInfo">If null, this will be pulled using 'memberName'.  If specified, then 'memberName' can be used to rename the property name.</param>
        /// <returns>An exception on error, or null on success.</returns>
        public Exception Bind(ObjectBinder obj, string memberName,
            out V8NativeObjectPropertyGetter getter, out V8NativeObjectPropertySetter setter,
            PropertyInfo propInfo = null)
        {
            getter = null;
            setter = null;

            if (obj == null) return new ArgumentNullException("'obj' is null.");

            if (propInfo == null)
            {
                if (obj.ObjectType == null) return new InvalidOperationException("'obj.ObjectType' is null.");

                propInfo = obj.ObjectType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (propInfo == null)
                    return new ArgumentNullException("'propInfo' cannot be determined - the object property '" + memberName + "' cannot be found/accessed.");
            }

            if (string.IsNullOrEmpty(memberName))
                memberName = propInfo.Name;

            if (propInfo.PropertyType == typeof(bool))
                getter = CreateGetAccessor<bool>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(byte))
                getter = CreateGetAccessor<byte>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(sbyte))
                getter = CreateGetAccessor<sbyte>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(Int16))
                getter = CreateGetAccessor<Int16>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(UInt16))
                getter = CreateGetAccessor<UInt16>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(Int32))
                getter = CreateGetAccessor<Int32>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(UInt32))
                getter = CreateGetAccessor<UInt32>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(Int64))
                getter = CreateGetAccessor<Int64>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(UInt64))
                getter = CreateGetAccessor<UInt64>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(Single))
                getter = CreateGetAccessor<Single>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(float))
                getter = CreateGetAccessor<float>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(double))
                getter = CreateGetAccessor<double>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(string))
                getter = CreateGetAccessor<string>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(char))
                getter = CreateGetAccessor<char>(obj, propInfo);

            else if (propInfo.PropertyType == typeof(DateTime))
                getter = CreateGetAccessor<DateTime>(obj, propInfo);
            else if (propInfo.PropertyType == typeof(TimeSpan))
                getter = CreateGetAccessor<TimeSpan>(obj, propInfo);

            else
                return new NotSupportedException("Member '" + memberName + "' is of an unsupported type '" + propInfo.PropertyType.Name + "'.");

            setter = CreateSetAccessor(obj, propInfo);

            return null;
        }

        public V8NativeObjectPropertySetter CreateSetAccessor(ObjectBinder obj, PropertyInfo propInfo)
        {
            if (obj == null) throw new ArgumentNullException("'obj' is null.");

            return (InternalHandle _this, string propertyName, InternalHandle value) => { propInfo.SetValue(obj.Object, value.Value, null); return InternalHandle.Empty; };
        }

        public V8NativeObjectPropertyGetter CreateGetAccessor<T>(ObjectBinder obj, PropertyInfo propInfo)
        {
            if (obj == null) throw new InvalidOperationException("'obj' is null.");

            var isSystemType = obj.ObjectType.Namespace == "System";

            if (isSystemType)
                return (InternalHandle _this, string propertyName) =>
                {
                    // (note: it's an error to read some properties in special cases on certain system type instances (such as 'Type.GenericParameterPosition'), so just ignore and return the message on system instances)
                    try { return CreateValue((T)propInfo.GetValue(obj.Object, null)); }
                    catch (Exception ex) { return CreateValue(Exceptions.GetFullErrorMessage(ex)); }
                };
            else
                return (InternalHandle _this, string propertyName) =>
                {
                    return CreateValue((T)propInfo.GetValue(obj.Object, null));
                };
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Binds a specific or named method of the specified object to a 'V8Function' callback wrapper.
        /// The returns function can be used in setting native V8 object properties to function values.
        /// </summary>
        /// <param name="obj">The object that contains the method to bind to, or null if 'methodInfo' is supplied and specifies a static method.</param>
        /// <param name="memberName">Required only if 'methodInfo' is null.</param>
        /// <param name="func">The 'V8Function' wrapper for specified method.</param>
        /// <param name="methodInfo">If you already have a 'MethodInfo' instance for the method, then you can speed up the binding process by passing it in here.
        /// If this is null, then the first method found with the same name as 'memberName' is assumed.</param>
        /// <param name="className">An optional name to return when 'valueOf()' is called on a JS object (this defaults to the method's name).</param>
        /// <param name="recursive">When an object is bound, only the object instance itself is bound (and not any reference members). If true, then nested object references are included.</param>
        /// <returns>An exception error object if an error occurs.</returns>
        public Exception Bind(ObjectBinder obj, string memberName, out V8Function func, MethodInfo methodInfo = null, string className = null, bool recursive = false)
        {
            func = null;

            if (methodInfo == null)
            {
                if (obj == null) return new ArgumentNullException("'obj' is null.");
                if (obj.ObjectType == null) return new InvalidOperationException("'obj.ObjectType' is null.");

                methodInfo = obj.ObjectType.GetMethod(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (methodInfo == null)
                    return new ArgumentNullException("'methodInfo' cannot be determined - the object method '" + memberName + "' cannot be found/accessed.");
            }

            if (string.IsNullOrEmpty(memberName))
                memberName = methodInfo.Name;

            var expectedParameters = methodInfo.GetParameters();
            var convertedArguments = new object[expectedParameters.Length];
            int i;

            var funcTemplate = CreateFunctionTemplate(className ?? memberName);

            func = funcTemplate.GetFunctionObject<V8Function>((V8Engine engine, bool isConstructCall, InternalHandle _this, InternalHandle[] args) =>
            {
                try
                {
                    for (i = 0; i < expectedParameters.Length; i++)
                        convertedArguments[i] = Types.ChangeType((i < args.Length) ? args[i].Value : 0, expectedParameters[i].ParameterType);

                    return CreateValue(methodInfo.Invoke(obj.Object, convertedArguments), recursive);
                }
                catch (Exception ex)
                {
                    return CreateError(Exceptions.GetFullErrorMessage(ex), JSValueType.ExecutionError);
                }
            });

            return null;
        }

        // --------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Creates a binding for a given CLR type to expose it in the JavaScript environment.
        /// The type returned is a function (V8Function) object that can be used to create the underlying type.
        /// <para>Note: Creating bindings is a much slower process than creating your own function templates.</para>
        /// </summary>
        /// <param name="className">A custom type name, or 'null' to use either the type name as is (the default), or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">When an object type is instantiate within JavaScript, only the object instance itself is bound (and not any reference members).
        /// <param name="memberAttributes">Default member attributes for members that don't have the 'ScriptMember' attribute.</param>
        /// If true, then nested object references are included.</param>
        public V8Function CreateBinding(Type type, string className = null, bool recursive = false, V8PropertyAttributes memberAttributes = V8PropertyAttributes.None)
        {
            TypeBinder.RegisterType(type, recursive, memberAttributes);

            var scriptObjectAttribute = (from a in type.GetCustomAttributes(true) where a is ScriptObject select (ScriptObject)a).FirstOrDefault();

            if (string.IsNullOrEmpty(className))
            {
                if (scriptObjectAttribute != null)
                    className = scriptObjectAttribute.TypeName;
                if (string.IsNullOrEmpty(className))
                    className = type.Name;
            }

            if (memberAttributes == V8PropertyAttributes.None)
                memberAttributes = (V8PropertyAttributes)scriptObjectAttribute.Security;

            var funcTemp = CreateFunctionTemplate(className);
            var func = funcTemp.GetFunctionObject<V8Function>((engine, isConstructCall, _this, args) =>
            {
                Handle handle = Handle.Empty;

                if (isConstructCall)
                {
                    var _args = new object[args.Length];
                    for (var i = 0; i < args.Length; i++)
                        _args[i] = args[i].Value;
                    handle = CreateBinding(Activator.CreateInstance(type, _args), recursive, memberAttributes);
                }

                return handle;
            });

            return func;
        }

        /// <summary>
        /// Creates a binding for a given CLR type to expose it in the JavaScript environment.
        /// The type returned is a function that can be used to create the underlying type.
        /// <para>Note: Creating bindings is a much slower process than creating your own function templates.</para>
        /// <param name="className">A custom type name, or 'null' to use either the type name as is (the default), or any existing 'ScriptObject' attribute name.</param>
        /// <param name="recursive">When an object type is instantiate within JavaScript, only the object itself is bound. If true, then nested object based properties are included.</param>
        /// <param name="memberAttributes">Default member attributes for members that don't have the 'ScriptMember' attribute.</param>
        public V8Function CreateBinding<T>(string className = null, bool recursive = false, V8PropertyAttributes memberAttributes = V8PropertyAttributes.None)
        {
            return CreateBinding(typeof(T), className, recursive, memberAttributes);
        }

        /// <summary>
        /// Creates a binding for a given CLR object instance to expose it in the JavaScript environment (sub-object members are not bound however).
        /// The type returned is an object with property accessors for the object's public fields, properties, and methods.
        /// <para>Note: Creating bindings is a much slower process than creating your own 'V8NativeObject' types.</para>
        /// </summary>
        /// <param name="recursive">For object types, if true, then nested objects are included, otherwise only the object itself is bound and returned.</param>
        public ObjectBinder CreateBinding(object obj, bool recursive = false, V8PropertyAttributes memberAttributes = V8PropertyAttributes.Undefined)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            TypeBinder.RegisterType(obj.GetType(), recursive, memberAttributes);

            var nObj = CreateObject<ObjectBinder>();
            nObj.Object = obj;

            return nObj;
        }

        // --------------------------------------------------------------------------------------------------------------------
    }

    // ========================================================================================================================
}
