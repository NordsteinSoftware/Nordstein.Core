using System.Reflection.Metadata;

namespace Nordstein.Core.Testing.Fences.Internal;

/// <summary>
/// Names for the entries of an assembly's metadata tables, for the reference scans.
/// </summary>
internal static class MetadataNames
{
    /// <summary>
    /// The full name of a type reference: <c>Namespace.Type</c>, a nested type as
    /// <c>Namespace.Outer+Inner</c>.
    /// </summary>
    public static string FullName(this MetadataReader metadata, TypeReferenceHandle handle)
    {
        TypeReference type = metadata.GetTypeReference(handle);
        string name = metadata.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? metadata.FullName((TypeReferenceHandle)type.ResolutionScope) + "+" + name
            : Qualified(metadata.GetString(type.Namespace), name);
    }

    /// <summary>
    /// The name of the type a member reference belongs to: a type reference's full name; for a
    /// generic instantiation, its generic type's (<c>System.Collections.Generic.List`1</c>); for a
    /// vararg call site, the type declaring the method; for any other type specification (an
    /// array, a generic parameter) or parent its kind in angle brackets.
    /// </summary>
    public static string ParentName(this MetadataReader metadata, EntityHandle parent)
        => parent.Kind switch
        {
            HandleKind.TypeReference => metadata.FullName((TypeReferenceHandle)parent),
            HandleKind.TypeSpecification => metadata.SpecificationName((TypeSpecificationHandle)parent),
            HandleKind.MethodDefinition => metadata.DefinitionName(metadata.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()),
            _ => "<" + parent.Kind + ">",
        };

    private static string SpecificationName(this MetadataReader metadata, TypeSpecificationHandle handle)
    {
        BlobReader signature = metadata.GetBlobReader(metadata.GetTypeSpecification(handle).Signature);
        SignatureTypeCode code = signature.ReadSignatureTypeCode();
        if (code != SignatureTypeCode.GenericTypeInstance)
        {
            return "<" + code + ">";
        }

        // GENERICINST (CLASS | VALUETYPE) TypeDefOrRef GenArgCount Type*: the generic type follows its kind.
        _ = signature.ReadSignatureTypeCode();
        EntityHandle generic = signature.ReadTypeHandle();
        return generic.Kind == HandleKind.TypeReference
            ? metadata.FullName((TypeReferenceHandle)generic)
            : metadata.DefinitionName((TypeDefinitionHandle)generic);
    }

    private static string DefinitionName(this MetadataReader metadata, TypeDefinitionHandle handle)
    {
        TypeDefinition type = metadata.GetTypeDefinition(handle);
        string name = metadata.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? Qualified(metadata.GetString(type.Namespace), name)
            : metadata.DefinitionName(declaring) + "+" + name;
    }

    // A type name never starts with a dot, so trimming one drops only the separator of an empty
    // (global) namespace.
    private static string Qualified(string ns, string name) => (ns + "." + name).TrimStart('.');
}
