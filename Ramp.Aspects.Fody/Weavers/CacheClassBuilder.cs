using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Ramp.Aspects.Fody.Weavers
{
    /// <summary>
    /// Used to build the class that caches instances of 
    /// </summary>
    internal class CacheClassBuilder
    {
        private readonly ModuleDefinition _module;
        private TypeDefinition _aspectCacheFieldsType;
        //private MethodDefinition _aspectCacheFieldsTypeConstructor;

        internal CacheClassBuilder(ModuleDefinition module)
        {
            _module = module;
        }

        internal FieldDefinition AddAspectCacheField(TypeReference aspectType)
        {
            // Initialize the type on first call
            if (_aspectCacheFieldsType == null)
            {
                var tattrs = TypeAttributes.Class |
                             TypeAttributes.Abstract |
                             TypeAttributes.Sealed |
                             TypeAttributes.BeforeFieldInit |
                             TypeAttributes.NotPublic;

                _aspectCacheFieldsType = new TypeDefinition(null, "<>z__AspectCache", tattrs, _module.TypeSystem.Object);

                _module.Types.Add(_aspectCacheFieldsType);

                //var mattrs = MethodAttributes.Private |
                //             MethodAttributes.Static |
                //             MethodAttributes.HideBySig |
                //             MethodAttributes.SpecialName |
                //             MethodAttributes.RTSpecialName;

                //_aspectCacheFieldsTypeConstructor = new MethodDefinition(".cctor", mattrs, _module.TypeSystem.Void);

                //_aspectCacheFieldsType.Methods.Add(_aspectCacheFieldsTypeConstructor);
            }

            var fattrs = FieldAttributes.CompilerControlled |
                         FieldAttributes.Assembly |
                         FieldAttributes.Static;
                         //FieldAttributes.InitOnly |

            string fname = "a" + _aspectCacheFieldsType.Fields.Count;

            var f = new FieldDefinition(fname, fattrs, _module.Import(aspectType));

            _aspectCacheFieldsType.Fields.Add(f);

            // Append initialization code to the cache static constructor

            //MethodDefinition defaultConstructor =
            //    aspectType.GetConstructors().Single(m => m.Parameters.Count == 1 && m.HasThis);

            //ILProcessor i = _aspectCacheFieldsTypeConstructor.Body.GetILProcessor();
            //i.Emit(OpCodes.Newobj, defaultConstructor);
            //i.Emit(OpCodes.Stsfld, f);

            return f;
        }
    }
}
