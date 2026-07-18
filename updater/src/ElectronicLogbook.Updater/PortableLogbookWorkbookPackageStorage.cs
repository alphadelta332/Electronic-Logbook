using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ElectronicLogbook.Portable;

namespace ElectronicLogbook.Updater;

public static class PortableLogbookWorkbookPackageStorage
{
    private static readonly XNamespace ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace PortableNamespace = PortableLogbookWorkbookMetadata.CustomXmlNamespace;

    public static void WriteEnvelope(string workbookPath, PortableLogbookWorkbookStorageEnvelope envelope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentNullException.ThrowIfNull(envelope);

        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        WriteXmlEntry(archive, PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath, CreateEnvelopeXml(envelope));
        EnsureContentType(archive);
        EnsureCustomXmlRelationship(archive);
    }

    public static PortableLogbookWorkbookStorageEnvelope? ReadEnvelope(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        using var archive = ZipFile.OpenRead(workbookPath);
        var entry = archive.GetEntry(PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var encodedJson = document.Root?.Element(PortableNamespace + "json")?.Value;
        if (string.IsNullOrWhiteSpace(encodedJson))
        {
            throw new InvalidDataException("Portable logbook workbook storage part is missing its JSON payload.");
        }

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedJson));
        return PortableLogbookWorkbookStorage.Deserialize(json);
    }

    public static bool CopyEnvelope(string sourceWorkbookPath, string destinationWorkbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationWorkbookPath);

        var envelope = ReadEnvelope(sourceWorkbookPath);
        if (envelope is null)
        {
            return false;
        }

        WriteEnvelope(destinationWorkbookPath, envelope);
        return true;
    }

    private static XDocument CreateEnvelopeXml(PortableLogbookWorkbookStorageEnvelope envelope)
    {
        var json = PortableLogbookWorkbookStorage.Serialize(envelope);
        return new XDocument(
            new XElement(
                PortableNamespace + "portableLogbookStorage",
                new XAttribute("version", PortableLogbookWorkbookStorage.CurrentStorageVersion),
                new XElement(PortableNamespace + "json", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)))));
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer);
    }

    private static void EnsureContentType(ZipArchive archive)
    {
        var document = ReadXmlEntry(archive, "[Content_Types].xml") ?? new XDocument(
            new XElement(
                ContentTypesNamespace + "Types",
                new XElement(
                    ContentTypesNamespace + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(
                    ContentTypesNamespace + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml"))));
        var root = document.Root ?? throw new InvalidDataException("Workbook content types part is invalid.");
        var partName = "/" + PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath;
        var hasOverride = root
            .Elements(ContentTypesNamespace + "Override")
            .Any(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase));
        if (!hasOverride)
        {
            root.Add(new XElement(
                ContentTypesNamespace + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", "application/xml")));
        }

        WriteXmlEntry(archive, "[Content_Types].xml", document);
    }

    private static void EnsureCustomXmlRelationship(ZipArchive archive)
    {
        var document = ReadXmlEntry(archive, "_rels/.rels") ?? new XDocument(new XElement(RelationshipsNamespace + "Relationships"));
        var root = document.Root ?? throw new InvalidDataException("Workbook relationships part is invalid.");
        var target = PortableLogbookWorkbookMetadata.StorageCustomXmlPartPath;
        var relationship = root
            .Elements(RelationshipsNamespace + "Relationship")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Id"), "rIdPortableLogbookStorage", StringComparison.Ordinal));
        if (relationship is null)
        {
            root.Add(new XElement(
                RelationshipsNamespace + "Relationship",
                new XAttribute("Id", "rIdPortableLogbookStorage"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"),
                new XAttribute("Target", target)));
        }
        else
        {
            relationship.SetAttributeValue("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml");
            relationship.SetAttributeValue("Target", target);
        }

        WriteXmlEntry(archive, "_rels/.rels", document);
    }

    private static XDocument? ReadXmlEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
