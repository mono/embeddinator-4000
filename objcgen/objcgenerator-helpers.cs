using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using IKVM.Reflection;
using Type = IKVM.Reflection.Type;

using Embeddinator;
namespace ObjC {
	public partial class ObjCProcessor {

		// get a name that is safe to use from ObjC code

		public void GetSignatures (string objName, string monoName, MemberInfo info, ParameterInfo [] parameters, bool useTypeNames, bool isExtension, out string objcSignature, out string monoSignature)
		{
			var method = (info as MethodBase); // else it's a PropertyInfo
			// special case for setter-only - the underscore looks ugly
			if ((method != null) && method.IsSpecialName)
				objName = objName.Replace ("_", String.Empty);

			var objc = new StringBuilder (objName);
			var mono = new StringBuilder (monoName);

			mono.Append ('(');

			for (int n = 0; n < parameters.Length; ++n) {
				ParameterInfo p = parameters [n];

				if (objc.Length > objName.Length) {
					objc.Append (' ');
					mono.Append (',');
				}

				string paramName = useTypeNames ? p.ParameterType.Name : p.Name;
				if ((method != null) && (n > 0 || !isExtension)) {
					if (n == 0) {
						bool mutatePropertyOrOperatorMethod = useTypeNames && (method.IsPropertyMethod () || method.IsOperatorMethod ());
						if (method.IsConstructor || mutatePropertyOrOperatorMethod || !method.IsSpecialName)
							objc.Append (paramName.PascalCase ());
					} else
						objc.Append (paramName.ToLowerInvariant ());
				}

				if (n > 0 || !isExtension) {
					string ptname = NameGenerator.GetObjCParamTypeName (p, Types);
					objc.Append (":(").Append (ptname).Append (")").Append (NameGenerator.GetExtendedParameterName (p, parameters));
				}
				mono.Append (NameGenerator.GetMonoName (p.ParameterType));
			}

			mono.Append (')');

			objcSignature = objc.ToString ();
			monoSignature = mono.ToString ();
		}

		public static string GetArrayCreator (string parameterName, Type type)
		{
			string arrayCreator = $"MonoArray* __{parameterName}arr = mono_array_new (__mono_context.domain, {{0}}, __{parameterName}length);";

			switch (Type.GetTypeCode (type)) {
			case TypeCode.String:
				return string.Format (arrayCreator, "mono_get_string_class ()");
			case TypeCode.Boolean:
				return string.Format (arrayCreator, "mono_get_boolean_class ()");
			case TypeCode.Char:
				return string.Format (arrayCreator, "mono_get_char_class ()");
			case TypeCode.SByte:
				return string.Format (arrayCreator, "mono_get_sbyte_class ()");
			case TypeCode.Int16:
				return string.Format (arrayCreator, "mono_get_int16_class ()");
			case TypeCode.Int32:
				return string.Format (arrayCreator, "mono_get_int32_class ()");
			case TypeCode.Int64:
				return string.Format (arrayCreator, "mono_get_int64_class ()");
			case TypeCode.Byte:
				return string.Format (arrayCreator, "mono_get_byte_class ()");
			case TypeCode.UInt16:
				return string.Format (arrayCreator, "mono_get_uint16_class ()");
			case TypeCode.UInt32:
				return string.Format (arrayCreator, "mono_get_uint32_class ()");
			case TypeCode.UInt64:
				return string.Format (arrayCreator, "mono_get_uint64_class ()");
			case TypeCode.Single:
				return string.Format (arrayCreator, "mono_get_single_class ()");
			case TypeCode.Double:
				return string.Format (arrayCreator, "mono_get_double_class ()");
			case TypeCode.Object:
				return string.Format (arrayCreator, $"{NameGenerator.GetObjCName (type)}_class");
			default:
				throw new NotImplementedException ($"Converting type {type.FullName} to mono class");
			}
		}
	}
}
