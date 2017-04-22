using System;
using System.Collections.Generic;
using System.Linq;

using IKVM.Reflection;
using Type = IKVM.Reflection.Type;

using Embeddinator;

namespace ObjC {

	public partial class ObjCGenerator {

		List<Exception> delayed = new List<Exception> ();
		HashSet<Type> unsupported = new HashSet<Type> ();

		bool IsSupported (Type t)
		{
			if (t.IsByRef)
				return IsSupported (t.GetElementType ());

			if (unsupported.Contains (t))
				return false;

			// FIXME protocols
			if (t.IsInterface) {
				delayed.Add (ErrorHelper.CreateWarning (1010, $"Type `{t}` is not generated because `interfaces` are not supported."));
				unsupported.Add (t);
				return false;
			}

			if (t.IsGenericParameter || t.IsGenericType) {
				delayed.Add (ErrorHelper.CreateWarning (1010, $"Type `{t}` is not generated because `generics` are not supported."));
				unsupported.Add (t);
				return false;
			}

			switch (t.Namespace) {
			case "System":
				switch (t.Name) {
				case "Object": // we cannot accept arbitrary NSObject (which we might not have bound) into mono
				case "Exception":
				case "IFormatProvider":
				case "Type":
					delayed.Add (ErrorHelper.CreateWarning (1011, $"Type `{t}` is not generated because it lacks a native counterpart."));
					unsupported.Add (t);
					return false;
				case "DateTime": // FIXME: NSDateTime
				case "Decimal": // FIXME: NSDecimal
				case "TimeSpan":
					delayed.Add (ErrorHelper.CreateWarning (1012, $"Type `{t}` is not generated because it lacks a marshaling code with a native counterpart."));
					unsupported.Add (t);
					return false;
				}
				break;
			}
			return true;
		}

		protected IEnumerable<Type> GetTypes (Assembly a)
		{
			foreach (var t in a.GetTypes ()) {
				if (!t.IsPublic)
					continue;

				if (!IsSupported (t))
					continue;

				yield return t;
			}
		}

		protected IEnumerable<ConstructorInfo> GetConstructors (Type t)
		{
			foreach (var ctor in t.GetConstructors ()) {
				// .cctor not to be called directly by native code
				if (ctor.IsStatic)
					continue;
				if (!ctor.IsPublic)
					continue;

				bool pcheck = true;
				foreach (var p in ctor.GetParameters ()) {
					var pt = p.ParameterType;
					if (!IsSupported (pt)) {
						delayed.Add (ErrorHelper.CreateWarning (1020, $"Constructor `{ctor}` is not generated because of parameter type `{pt}` is not supported."));
						pcheck = false;
					} else if (p.HasDefaultValue) {
						delayed.Add (ErrorHelper.CreateWarning (1021, $"Constructor `{ctor}` parameter `{p.Name}` has a default value that is not supported."));
					}
				}
				if (!pcheck)
					continue;

				yield return ctor;
			}
		}

		Dictionary<Type,MethodInfo> icomparable = new Dictionary<Type, MethodInfo> ();
		Dictionary<Type, MethodInfo> equals = new Dictionary<Type, MethodInfo> ();
		Dictionary<Type, MethodInfo> hashes = new Dictionary<Type, MethodInfo> ();

		// defining type / extended type / methods
		Dictionary<Type, Dictionary<Type, List<MethodInfo>>> extensions_methods = new Dictionary<Type, Dictionary<Type, List<MethodInfo>>> ();

		protected IEnumerable<MethodInfo> GetMethods (Type t)
		{
			foreach (var mi in t.GetMethods (BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				if (!mi.IsPublic)
					continue;

				// handle special cases where we can implement something better, e.g. a better match
				if (implement_system_icomparable_t) {
					// for X we prefer `IComparable<X>` to `IComparable` - since it will be exposed identically to ObjC
					if (mi.Match ("System.Int32", "CompareTo", t.FullName)) {
						icomparable [t] = mi;
						continue;
					}
				}
				if (implement_system_icomparable && mi.Match ("System.Int32", "CompareTo", "System.Object")) {
					// don't replace CompareTo(T) with CompareTo(Object)
					if (!icomparable.ContainsKey (t))
						icomparable.Add (t, mi);
					continue;
				}

				if (mi.Match ("System.Boolean", "Equals", "System.Object")) {
					equals.Add (t, mi);
					continue;
				}

				if (mi.Match ("System.Int32", "GetHashCode")) {
					hashes.Add (t, mi);
					continue;
				}

				// handle extension methods
				if (extension_type && mi.HasCustomAttribute ("System.Runtime.CompilerServices", "ExtensionAttribute")) {
					Dictionary<Type, List<MethodInfo>> extensions;
					if (!extensions_methods.TryGetValue (t, out extensions)) {
						extensions = new Dictionary<Type, List<MethodInfo>> ();
						extensions_methods.Add (t, extensions);
					}
					var extended_type = mi.GetParameters () [0].ParameterType;
					List<MethodInfo> methods;
					if (!extensions.TryGetValue (extended_type, out methods)) {
						methods = new List<MethodInfo> ();
						extensions.Add (extended_type, methods);
					}
					methods.Add (mi);
					continue;
				}

				var rt = mi.ReturnType;
				if (!IsSupported (rt)) {
					delayed.Add (ErrorHelper.CreateWarning (1030, $"Method `{mi}` is not generated because return type `{rt}` is not supported."));
					continue;
				}

				bool pcheck = true;
				foreach (var p in mi.GetParameters ()) {
					var pt = p.ParameterType;
					if (!IsSupported (pt)) {
						delayed.Add (ErrorHelper.CreateWarning (1031, $"Method `{mi}` is not generated because of parameter type `{pt}` is not supported."));
						pcheck = false;
					} else if (p.HasDefaultValue) {
						delayed.Add (ErrorHelper.CreateWarning (1032, $"Method `{mi}` parameter `{p.Name}` has a default value that is not supported."));
					}
				}
				if (!pcheck)
					continue;

				yield return mi;
			}
		}

		protected IEnumerable<PropertyInfo> GetProperties (Type t)
		{
			foreach (var pi in t.GetProperties (BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				var pt = pi.PropertyType;
				if (!IsSupported (pt)) {
					delayed.Add (ErrorHelper.CreateWarning (1040, $"Property `{pi}` is not generated because of parameter type `{pt}` is not supported."));
					continue;
				}
				yield return pi;
			}
		}

		protected IEnumerable<FieldInfo> GetFields (Type t)
		{
			foreach (var fi in t.GetFields (BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
				if (!fi.IsPublic)
					continue;
				var ft = fi.FieldType;
				if (!IsSupported (ft)) {
					delayed.Add (ErrorHelper.CreateWarning (1050, $"Field `{fi}` is not generated because of field type `{ft}` is not supported."));
					continue;
				}
				yield return fi;
			}
		}

		List<Type> enums = new List<Type> ();
		List<Type> types = new List<Type> ();
		Dictionary<Type, List<ConstructorInfo>> ctors = new Dictionary<Type, List<ConstructorInfo>> ();
		Dictionary<Type, List<MethodInfo>> methods = new Dictionary<Type, List<MethodInfo>> ();
		Dictionary<Type, List<PropertyInfo>> properties = new Dictionary<Type, List<PropertyInfo>> ();
		Dictionary<Type, List<FieldInfo>> fields = new Dictionary<Type, List<FieldInfo>> ();
		Dictionary<Type, List<PropertyInfo>> subscriptProperties = new Dictionary<Type, List<PropertyInfo>> ();

		// special cases
		bool implement_system_icomparable;
		bool implement_system_icomparable_t;
		bool extension_type;

		public override void Process (IEnumerable<Assembly> assemblies)
		{
			foreach (var a in assemblies) {
				foreach (var t in GetTypes (a)) {
					if (t.IsEnum) {
						enums.Add (t);
						continue;
					}

					types.Add (t);

					extension_type = t.HasCustomAttribute ("System.Runtime.CompilerServices", "ExtensionAttribute");

					implement_system_icomparable = t.Implements ("System", "IComparable");
					implement_system_icomparable_t = t.Implements("System", "IComparable`1");

					var constructors = GetConstructors (t).OrderBy ((arg) => arg.ParameterCount).ToList ();
					if (constructors.Count > 0)
						ctors.Add (t, constructors);

					var meths = GetMethods (t).OrderBy ((arg) => arg.Name).ToList ();
					if (meths.Count > 0)
						methods.Add (t, meths);

					var props = new List<PropertyInfo> ();
					var subscriptProps = new List<PropertyInfo> ();
					foreach (var pi in GetProperties (t)) {
						var getter = pi.GetGetMethod ();
						var setter = pi.GetSetMethod ();
						// setter only property are valid in .NET and we need to generate a method in ObjC (there's no writeonly properties)
						if (getter == null)
							continue;

						// indexers are implemented as methods and object subscripting
						if ((getter.ParameterCount > 0) || ((setter != null) && setter.ParameterCount > 1)) {
							subscriptProps.Add (pi);
							continue;
						}

						// we can do better than methods for the more common cases (readonly and readwrite)
						meths.Remove (getter);
						meths.Remove (setter);
						props.Add (pi);
					}
					props = props.OrderBy ((arg) => arg.Name).ToList ();
					if (props.Count > 0)
						properties.Add (t, props);

					if (subscriptProps.Count > 0) {
						if (subscriptProps.Count > 1)
							delayed.Add (ErrorHelper.CreateWarning (1041, $"Indexed properties on {t.Name} is not generated because multiple indexed properties not supported."));
						else
							subscriptProperties.Add (t, subscriptProps);
					}

					// fields will need to be wrapped within properties
					var f = GetFields (t).OrderBy ((arg) => arg.Name).ToList ();
					if (f.Count > 0)
						fields.Add (t, f);
				}
			}
			types = types.OrderBy ((arg) => arg.FullName).OrderBy ((arg) => types.Contains (arg.BaseType)).ToList ();
			Console.WriteLine ($"\t{types.Count} types found");

			ErrorHelper.Show (delayed);
		}
	}
}
