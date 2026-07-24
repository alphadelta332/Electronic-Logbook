using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileLogbookDocument
{
    public static PortableLogbookDocumentV2 AppendOperation(
        PortableLogbookDocumentV2 document,
        IEnumerable<CustomFieldDefinition> requiredCustomFields,
        PortableLogbookOperationV2 operation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(requiredCustomFields);
        ArgumentNullException.ThrowIfNull(operation);

        var customFields = PortableLogbookCustomFieldDefinitions
            .Merge(document.CustomFieldDefinitions, requiredCustomFields)
            .Definitions;
        return PortableLogbookDocumentV2.CreateAustraliaFirst(
            document.LogbookId,
            customFields,
            document.CurrencyOverrideDates,
            document.Operations.Concat([operation]));
    }

    public static PortableLogbookDocument AppendOperation(
        PortableLogbookDocument document,
        IEnumerable<CustomFieldDefinition> requiredCustomFields,
        PortableLogbookOperation operation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(requiredCustomFields);
        ArgumentNullException.ThrowIfNull(operation);

        var customFields = PortableLogbookCustomFieldDefinitions
            .Merge(document.CustomFieldDefinitions, requiredCustomFields)
            .Definitions;
        return PortableLogbookDocument.CreateAustraliaFirst(
            document.LogbookId,
            customFields,
            document.Operations.Concat([operation]));
    }
}
