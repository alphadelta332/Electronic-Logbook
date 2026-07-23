namespace ElectronicLogbook.Portable;

public static class PortableLogbookCustomFieldSet
{
    public const int WorkbookCustomFieldCount = 4;

    public static IReadOnlyList<CustomFieldDefinition> CreateWorkbookCustomFields(
        IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var labelArray = labels.ToArray();
        if (labelArray.Length != WorkbookCustomFieldCount)
        {
            throw new ArgumentException($"Exactly {WorkbookCustomFieldCount} workbook custom-field labels are required.", nameof(labels));
        }

        return labelArray
            .Select((label, index) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(labels));
                return new CustomFieldDefinition(
                    new CustomFieldId($"cf_workbook_{index + 1}"),
                    label.Trim(),
                    index + 1);
            })
            .ToArray();
    }
}
