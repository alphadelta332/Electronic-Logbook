using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Mobile;

public static class MobileLogbookDocument
{
    public static PortableLogbookDocumentV2 SetCurrencyOverrideDates(
        PortableLogbookDocumentV2 document,
        PortableLogbookCurrencyOverrideDates overrideDates)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(overrideDates);

        var updated = PortableLogbookDocumentV2.CreateAustraliaFirst(
            document.LogbookId,
            document.CustomFieldDefinitions,
            overrideDates,
            document.Operations);
        EnsureValid(updated);
        return updated;
    }

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
        var updated = PortableLogbookDocumentV2.CreateAustraliaFirst(
            document.LogbookId,
            customFields,
            document.CurrencyOverrideDates,
            document.Operations.Concat([operation]));
        EnsureValid(updated);
        return updated;
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
        var updated = PortableLogbookDocument.CreateAustraliaFirst(
            document.LogbookId,
            customFields,
            document.Operations.Concat([operation]));
        EnsureValid(updated);
        return updated;
    }

    private static void EnsureValid(PortableLogbookDocument document)
    {
        if (!PortableLogbookValidator.Validate(document).IsValid)
        {
            throw new ArgumentException("The appended portable logbook operation is invalid.", nameof(document));
        }
    }

    private static void EnsureValid(PortableLogbookDocumentV2 document)
    {
        if (!PortableLogbookValidatorV2.Validate(document).IsValid)
        {
            throw new ArgumentException("The appended portable logbook operation is invalid.", nameof(document));
        }
    }
}
