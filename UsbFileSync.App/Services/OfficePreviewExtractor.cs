using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using ExcelDataReader;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace UsbFileSync.App.Services;

public static class OfficePreviewExtractor
{
    private static bool _excelEncodingsRegistered;

    public static OfficePreviewExtractionResult ExtractPreview(string path, int characterLimit)
    {
        var extension = PreviewProviderDefaults.NormalizeExtension(Path.GetExtension(path));

        return extension switch
        {
            ".docx" or ".docm" or ".dotx" or ".dotm" => ExtractWordPreview(path, characterLimit),
            ".pptx" or ".pptm" or ".ppsx" or ".ppsm" or ".potx" or ".potm" => ExtractPowerPointPreview(path, characterLimit),
            ".xls" or ".xlsb" or ".xlsx" or ".xlsm" or ".xltx" or ".xltm" => ExtractExcelPreview(path, characterLimit),
            _ => new OfficePreviewExtractionResult { DiagnosticText = "Office preview is not configured for this file type." },
        };
    }

    public static OfficePreviewExtractionResult ExtractPreviewWithMode(string path, int characterLimit, OfficePreviewMode mode)
    {
        var extension = PreviewProviderDefaults.NormalizeExtension(Path.GetExtension(path));

        return mode switch
        {
            OfficePreviewMode.OpenXml => ExtractOpenXmlOnly(path, characterLimit, extension),
            OfficePreviewMode.OfficeInterop => ExtractInteropOnly(path, characterLimit, extension),
            _ => new OfficePreviewExtractionResult { DiagnosticText = "Use shell preview mode for this file." },
        };
    }

    private static OfficePreviewExtractionResult ExtractOpenXmlOnly(string path, int characterLimit, string extension)
    {
        try
        {
            var text = extension switch
            {
                ".docx" or ".docm" or ".dotx" or ".dotm" => ExtractWordPreviewWithOpenXml(path),
                ".pptx" or ".pptm" or ".ppsx" or ".ppsm" or ".potx" or ".potm" => ExtractPowerPointPreviewWithOpenXml(path),
                ".xlsx" or ".xlsm" or ".xltx" or ".xltm" => ExtractExcelPreviewWithDataReader(path),
                ".xls" or ".xlsb" => ExtractExcelPreviewWithDataReader(path),
                _ => string.Empty,
            };

            return !string.IsNullOrWhiteSpace(text)
                ? CreateSuccessfulResult(text, characterLimit)
                : new OfficePreviewExtractionResult { DiagnosticText = "Open XML SDK produced no readable content for this file." };
        }
        catch (Exception exception)
        {
            return new OfficePreviewExtractionResult
            {
                DiagnosticText = $"Open XML SDK: {exception.GetType().Name}: {exception.Message}",
            };
        }
    }

    private static OfficePreviewExtractionResult ExtractInteropOnly(string path, int characterLimit, string extension)
    {
        try
        {
            var text = extension switch
            {
                ".docx" or ".docm" or ".dotx" or ".dotm" => OfficeInteropPreviewExtractor.ExtractWordPreview(path),
                ".pptx" or ".pptm" or ".ppsx" or ".ppsm" or ".potx" or ".potm" => OfficeInteropPreviewExtractor.ExtractPowerPointPreview(path),
                ".xls" or ".xlsb" or ".xlsx" or ".xlsm" or ".xltx" or ".xltm" => OfficeInteropPreviewExtractor.ExtractExcelPreview(path),
                _ => string.Empty,
            };

            return !string.IsNullOrWhiteSpace(text)
                ? CreateSuccessfulResult(text, characterLimit)
                : new OfficePreviewExtractionResult { DiagnosticText = "Office automation produced no readable content for this file." };
        }
        catch (Exception exception)
        {
            return new OfficePreviewExtractionResult
            {
                DiagnosticText = $"Office automation: {exception.GetType().Name}: {exception.Message}",
            };
        }
    }

    private static OfficePreviewExtractionResult ExtractWordPreview(string path, int characterLimit)
    {
        var diagnostics = new List<string>();

        try
        {
            return CreateSuccessfulResult(ExtractWordPreviewWithOpenXml(path), characterLimit);
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Open XML SDK: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var interopPreview = OfficeInteropPreviewExtractor.ExtractWordPreview(path);
            if (!string.IsNullOrWhiteSpace(interopPreview))
            {
                return CreateSuccessfulResult(interopPreview, characterLimit);
            }

            diagnostics.Add("Office automation fallback: no readable document content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Office automation fallback: {exception.GetType().Name}: {exception.Message}");
        }

        return new OfficePreviewExtractionResult
        {
            DiagnosticText = BuildOpenXmlDiagnosticText(path, "Word", diagnostics),
        };
    }

    private static OfficePreviewExtractionResult BuildResult(Func<string> extractor, string path, int characterLimit, string documentLabel)
    {
        try
        {
            return CreateSuccessfulResult(extractor(), characterLimit);
        }
        catch (Exception exception)
        {
            return new OfficePreviewExtractionResult
            {
                DiagnosticText = BuildOpenXmlDiagnosticText(path, documentLabel, [$"Preview extraction failed.", $"{exception.GetType().Name}: {exception.Message}"]),
            };
        }
    }

    private static OfficePreviewExtractionResult ExtractPowerPointPreview(string path, int characterLimit)
    {
        var diagnostics = new List<string>();

        try
        {
            return CreateSuccessfulResult(ExtractPowerPointPreviewWithOpenXml(path), characterLimit);
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Open XML SDK: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var interopPreview = OfficeInteropPreviewExtractor.ExtractPowerPointPreview(path);
            if (!string.IsNullOrWhiteSpace(interopPreview))
            {
                return CreateSuccessfulResult(interopPreview, characterLimit);
            }

            diagnostics.Add("Office automation fallback: no readable slide content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Office automation fallback: {exception.GetType().Name}: {exception.Message}");
        }

        return new OfficePreviewExtractionResult
        {
            DiagnosticText = BuildOpenXmlDiagnosticText(path, "PowerPoint", diagnostics),
        };
    }

    private static string ExtractWordPreviewWithOpenXml(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var paragraphs = body
            .Descendants<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<WordText>().Select(text => text.Text)).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string ExtractPowerPointPreviewWithOpenXml(string path)
    {
        using var document = PresentationDocument.Open(path, false);
        var presentationPart = document.PresentationPart;
        var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<SlideId>() ?? [];

        var slideTexts = slideIds
            .Select((slideId, index) =>
            {
                var slidePart = presentationPart!.GetPartById(slideId.RelationshipId!) as SlidePart;
                var textRuns = slidePart?.Slide
                    .Descendants<DrawingText>()
                    .Select(text => text.Text?.Trim() ?? string.Empty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();
                if (textRuns is null || textRuns.Count == 0)
                {
                    return string.Empty;
                }

                return $"Slide {index + 1}{Environment.NewLine}{string.Join(Environment.NewLine, textRuns)}";
            })
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine + Environment.NewLine, slideTexts);
    }

    private static OfficePreviewExtractionResult ExtractExcelPreview(string path, int characterLimit)
    {
        var diagnostics = new List<string>();

        try
        {
            var dataReaderPreview = ExtractExcelPreviewWithDataReader(path);
            if (!string.IsNullOrWhiteSpace(dataReaderPreview))
            {
                return CreateSuccessfulResult(dataReaderPreview, characterLimit);
            }

            diagnostics.Add("ExcelDataReader: no readable worksheet content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"ExcelDataReader: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var openXmlPreview = ExtractExcelPreviewWithOpenXml(path);
            if (!string.IsNullOrWhiteSpace(openXmlPreview))
            {
                return CreateSuccessfulResult(openXmlPreview, characterLimit);
            }

            diagnostics.Add("Open XML SDK: no readable worksheet content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Open XML SDK: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var archivePreview = ExtractExcelPreviewFromArchive(path);
            if (!string.IsNullOrWhiteSpace(archivePreview))
            {
                return CreateSuccessfulResult(archivePreview, characterLimit);
            }

            diagnostics.Add("Archive XML fallback: no readable worksheet content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Archive XML fallback: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            var interopPreview = OfficeInteropPreviewExtractor.ExtractExcelPreview(path);
            if (!string.IsNullOrWhiteSpace(interopPreview))
            {
                return CreateSuccessfulResult(interopPreview, characterLimit);
            }

            diagnostics.Add("Office automation fallback: no readable worksheet content was found.");
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Office automation fallback: {exception.GetType().Name}: {exception.Message}");
        }

        return new OfficePreviewExtractionResult
        {
            DiagnosticText = BuildExcelDiagnosticText(path, diagnostics),
        };
    }

    private static OfficePreviewExtractionResult CreateSuccessfulResult(string preview, int characterLimit)
    {
        if (string.IsNullOrWhiteSpace(preview))
        {
            return new OfficePreviewExtractionResult
            {
                DiagnosticText = "Office document preview is empty.",
            };
        }

        if (preview.Length > characterLimit)
        {
            preview = preview[..characterLimit] + Environment.NewLine + Environment.NewLine + "[Preview truncated]";
        }

        return new OfficePreviewExtractionResult
        {
            PreviewText = preview,
        };
    }

    private static string BuildExcelDiagnosticText(string path, IEnumerable<string> diagnostics)
    {
        var lines = diagnostics.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var fileHint = GetExcelFileHint(path);
        if (!string.IsNullOrWhiteSpace(fileHint))
        {
            lines.Insert(0, fileHint);
        }

        if (lines.Count == 0)
        {
            lines.Add("No parser produced readable worksheet content.");
        }

        return $"Excel preview diagnostics{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string BuildOpenXmlDiagnosticText(string path, string documentLabel, IEnumerable<string> diagnostics)
    {
        var lines = new List<string>();
        var fileHint = GetOpenXmlFileHint(path, documentLabel);
        if (!string.IsNullOrWhiteSpace(fileHint))
        {
            lines.Add(fileHint);
        }

        lines.AddRange(diagnostics.Where(line => !string.IsNullOrWhiteSpace(line)));

        if (lines.Count == 0)
        {
            lines.Add("Preview extraction failed.");
        }

        return $"{documentLabel} preview diagnostics{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string GetOpenXmlFileHint(string path, string documentLabel)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[4];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead <= 0)
            {
                return "The file is empty.";
            }

            var isZipContainer = bytesRead >= 4
                && header[0] == 0x50
                && header[1] == 0x4B
                && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
                && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);

            if (isZipContainer)
            {
                return string.Empty;
            }

            return $"This file does not have a valid ZIP-based {documentLabel} document signature. It is likely renamed, incomplete, or corrupted.";
        }
        catch (Exception headerException)
        {
            return $"The file header could not be inspected. {headerException.GetType().Name}: {headerException.Message}";
        }
    }

    private static string GetExcelFileHint(string path)
    {
        var extension = PreviewProviderDefaults.NormalizeExtension(Path.GetExtension(path));

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[8];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead <= 0)
            {
                return "The file is empty.";
            }

            var isZipContainer = bytesRead >= 4
                && header[0] == 0x50
                && header[1] == 0x4B
                && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
                && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);

            var isCompoundBinary = bytesRead >= 8
                && header[0] == 0xD0
                && header[1] == 0xCF
                && header[2] == 0x11
                && header[3] == 0xE0
                && header[4] == 0xA1
                && header[5] == 0xB1
                && header[6] == 0x1A
                && header[7] == 0xE1;

            return extension switch
            {
                ".xlsx" or ".xlsm" or ".xlsb" or ".xltx" or ".xltm" when !isZipContainer =>
                    "This file does not have a valid ZIP-based Excel workbook signature. It is likely renamed, incomplete, or corrupted.",
                ".xls" when !isCompoundBinary =>
                    "This file does not have a valid legacy Excel workbook signature. It is likely renamed, incomplete, or corrupted.",
                _ => string.Empty,
            };
        }
        catch (Exception exception)
        {
            return $"The file header could not be inspected. {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static string ExtractExcelPreviewWithDataReader(string path)
    {
        EnsureExcelEncodingsRegistered();

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var sheetTexts = new List<string>();
        do
        {
            var rowTexts = new List<string>();
            while (reader.Read())
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(index => FormatExcelCellValue(reader.GetValue(index)))
                    .ToList();

                if (values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rowTexts.Add(string.Join("\t", values.Select(value => value ?? string.Empty).Where((_, index) => index < values.Count)));
            }

            if (rowTexts.Count == 0)
            {
                continue;
            }

            var sheetName = string.IsNullOrWhiteSpace(reader.Name)
                ? $"Sheet {sheetTexts.Count + 1}"
                : reader.Name;
            sheetTexts.Add($"{sheetName}{Environment.NewLine}{string.Join(Environment.NewLine, rowTexts)}");
        }
        while (reader.NextResult());

        return string.Join(Environment.NewLine + Environment.NewLine, sheetTexts);
    }

    private static string ExtractExcelPreviewWithOpenXml(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null)
        {
            return string.Empty;
        }

        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sheetTexts = workbookPart.Workbook.Sheets.Elements<Sheet>()
            .Select((sheet, index) =>
            {
                if (string.IsNullOrWhiteSpace(sheet.Id))
                {
                    return string.Empty;
                }

                WorksheetPart? worksheetPart;
                try
                {
                    worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
                }
                catch (Exception)
                {
                    return string.Empty;
                }

                var cellValues = worksheetPart?.Worksheet
                    .Descendants<Cell>()
                    .Select(cell => ReadCellValue(cell, sharedStringTable))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (cellValues is null || cellValues.Count == 0)
                {
                    return string.Empty;
                }

                var sheetName = string.IsNullOrWhiteSpace(sheet.Name?.Value)
                    ? $"Sheet {index + 1}"
                    : sheet.Name!.Value!;

                return $"{sheetName}{Environment.NewLine}{string.Join(Environment.NewLine, cellValues)}";
            })
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine + Environment.NewLine, sheetTexts);
    }

    private static string ExtractExcelPreviewFromArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
        {
            return string.Empty;
        }

        var workbookDocument = XDocument.Load(workbookEntry.Open(), LoadOptions.None);
        var sheetTargets = LoadWorkbookSheetTargets(archive);
        var sharedStrings = LoadArchiveSharedStrings(archive);

        var sheetTexts = workbookDocument.Descendants()
            .Where(element => element.Name.LocalName == "sheet")
            .Select((sheet, index) => CreateArchiveSheetPreview(sheet, index, archive, sheetTargets, sharedStrings))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine + Environment.NewLine, sheetTexts);
    }

    private static string CreateArchiveSheetPreview(
        XElement sheet,
        int index,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> sheetTargets,
        IReadOnlyList<string> sharedStrings)
    {
        var relationshipId = sheet.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
        var sheetName = sheet.Attribute("name")?.Value;
        var entryPath = ResolveSheetEntryPath(sheetTargets, relationshipId, index);
        var worksheetEntry = archive.GetEntry(entryPath);
        if (worksheetEntry is null)
        {
            return string.Empty;
        }

        var worksheetDocument = XDocument.Load(worksheetEntry.Open(), LoadOptions.None);
        var rowTexts = worksheetDocument.Descendants()
            .Where(element => element.Name.LocalName == "row")
            .Select(row => string.Join(
                "\t",
                row.Elements().Where(cell => cell.Name.LocalName == "c")
                    .Select(cell => ReadArchiveCellValue(cell, sharedStrings))
                    .Where(value => !string.IsNullOrWhiteSpace(value))))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (rowTexts.Count == 0)
        {
            return string.Empty;
        }

        var effectiveSheetName = string.IsNullOrWhiteSpace(sheetName)
            ? $"Sheet {index + 1}"
            : sheetName;

        return $"{effectiveSheetName}{Environment.NewLine}{string.Join(Environment.NewLine, rowTexts)}";
    }

    private static IReadOnlyDictionary<string, string> LoadWorkbookSheetTargets(ZipArchive archive)
    {
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relationshipsEntry is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var relationshipsDocument = XDocument.Load(relationshipsEntry.Open(), LoadOptions.None);
        return relationshipsDocument.Descendants()
            .Where(element => element.Name.LocalName == "Relationship")
            .Select(element => new
            {
                Id = element.Attribute("Id")?.Value,
                Target = element.Attribute("Target")?.Value,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Target))
            .ToDictionary(
                item => item.Id!,
                item => NormalizeZipEntryPath(item.Target!),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> LoadArchiveSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
        {
            return [];
        }

        var sharedStringsDocument = XDocument.Load(sharedStringsEntry.Open(), LoadOptions.None);
        return sharedStringsDocument.Descendants()
            .Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(text => text.Value)).Trim())
            .ToList();
    }

    private static string ResolveSheetEntryPath(IReadOnlyDictionary<string, string> sheetTargets, string? relationshipId, int index)
    {
        if (!string.IsNullOrWhiteSpace(relationshipId)
            && sheetTargets.TryGetValue(relationshipId, out var targetPath)
            && !string.IsNullOrWhiteSpace(targetPath))
        {
            return targetPath;
        }

        return $"xl/worksheets/sheet{index + 1}.xml";
    }

    private static string NormalizeZipEntryPath(string target)
    {
        var normalized = target.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/'))
        {
            normalized = normalized.TrimStart('/');
        }

        if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"xl/{normalized}";
        }

        return normalized;
    }

    private static string ReadArchiveCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var cellType = cell.Attribute("t")?.Value;
        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(text => text.Value)).Trim();
        }

        var rawValue = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            rawValue = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "f")?.Value;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rawValue, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        if (string.Equals(cellType, "b", StringComparison.OrdinalIgnoreCase))
        {
            return rawValue == "1" ? "TRUE" : "FALSE";
        }

        return rawValue.Trim();
    }

    private static void EnsureExcelEncodingsRegistered()
    {
        if (_excelEncodingsRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _excelEncodingsRegistered = true;
    }

    private static string FormatExcelCellValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss"),
        bool boolean => boolean ? "TRUE" : "FALSE",
        double number => number.ToString("G15"),
        float number => number.ToString("G9"),
        decimal number => number.ToString(),
        _ => value.ToString()?.Trim() ?? string.Empty,
    };

    private static string ReadCellValue(Cell cell, SharedStringTable? sharedStringTable)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText?.Trim() ?? string.Empty;
        }

        var value = cell.CellValue?.InnerText;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringTable?.ElementAt(sharedStringIndex) is SharedStringItem sharedStringItem)
        {
            return sharedStringItem.InnerText.Trim();
        }

        return value.Trim();
    }
}