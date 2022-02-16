using UnityEditor;
using UnityEngine;

namespace Unity.QuickSearch
{
    static class IndexerExtensions
    {
        [CustomObjectIndexer(typeof(Material))]
        internal static void MaterialShaderReferences(CustomObjectIndexerTarget context, ObjectIndexer indexer)
        {
            var material = context.target as Material;
            if (material == null)
                return;

            if (material.shader)
            {
                var fullShaderName = material.shader.name.ToLowerInvariant();
                var shortShaderName = System.IO.Path.GetFileNameWithoutExtension(fullShaderName);
                indexer.AddProperty("ref", shortShaderName, context.documentIndex, saveKeyword: false);
                indexer.AddProperty("ref", fullShaderName, context.documentIndex, saveKeyword: false);
            }

            if (!indexer.settings.options.properties)
                return;

            var properties = MaterialEditor.GetMaterialProperties(new Material[] { material });
            foreach (var property in properties)
            {
                var propertyName = property.name;
                if (propertyName.Length > 0 && propertyName[0] == '_')
                    propertyName = propertyName.Substring(1).ToLowerInvariant();
                switch (property.type)
                {
                    case MaterialProperty.PropType.Color:
                        indexer.AddProperty(propertyName, ColorUtility.ToHtmlStringRGB(property.colorValue).ToLowerInvariant(), context.documentIndex, exact: true, saveKeyword: true);
                        break;

                    case MaterialProperty.PropType.Vector:
                        indexer.AddNumber(propertyName + "x", property.vectorValue.x, indexer.settings.baseScore, context.documentIndex);
                        indexer.AddNumber(propertyName + "y", property.vectorValue.y, indexer.settings.baseScore, context.documentIndex);
                        indexer.AddNumber(propertyName + "z", property.vectorValue.z, indexer.settings.baseScore, context.documentIndex);
                        indexer.AddNumber(propertyName + "w", property.vectorValue.w, indexer.settings.baseScore, context.documentIndex);
                        break;

                    case MaterialProperty.PropType.Float:
                        indexer.AddNumber(propertyName, property.floatValue, indexer.settings.baseScore, context.documentIndex);
                        break;

                    case MaterialProperty.PropType.Range:
                        indexer.AddNumber(propertyName + "x", property.rangeLimits.x, indexer.settings.baseScore, context.documentIndex);
                        indexer.AddNumber(propertyName + "y", property.rangeLimits.x, indexer.settings.baseScore, context.documentIndex);
                        break;

                    case MaterialProperty.PropType.Texture:
                        if (property.textureValue)
                        {
                            indexer.AddProperty(propertyName, property.textureValue.name.ToLowerInvariant(), context.documentIndex, saveKeyword: true);
                            indexer.AddProperty("ref", AssetDatabase.GetAssetPath(property.textureValue).ToLowerInvariant(), context.documentIndex, exact: true, saveKeyword: true);
                        }
                        break;

                    default:
                        break;
                }
            }
        }

        [CustomObjectIndexer(typeof(MeshRenderer))]
        internal static void IndexMeshRendererMaterials(CustomObjectIndexerTarget context, ObjectIndexer indexer)
        {
            if (indexer.settings.type != "asset")
                return;

            var c = context.target as MeshRenderer;
            if (c == null)
                return;

            indexer.AddNumber("materialcount", c.sharedMaterials.Length, indexer.settings.baseScore + 2, context.documentIndex);
            foreach (var m in c.sharedMaterials)
            {
                indexer.AddProperty("material", m.name.Replace(" (Instance)", "").ToLowerInvariant(), context.documentIndex, saveKeyword: true);

                var mp = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(mp))
                    indexer.AddProperty("material", mp.ToLowerInvariant(), context.documentIndex, saveKeyword: false, exact: true);
            }
        }
    }
}
