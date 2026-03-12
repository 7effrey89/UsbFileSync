using System.Runtime.InteropServices;
using System.Text;

namespace UsbFileSync.App.Services;

internal static class OfficeInteropPreviewExtractor
{
    public static string ExtractPowerPointPreview(string path)
    {
        return RunInSta(() =>
        {
            var applicationType = Type.GetTypeFromProgID("PowerPoint.Application")
                ?? throw new InvalidOperationException("Microsoft PowerPoint is not installed.");

            object? application = null;
            object? presentations = null;
            object? presentation = null;
            object? slides = null;

            try
            {
                application = Activator.CreateInstance(applicationType);
                presentations = applicationType.InvokeMember("Presentations", System.Reflection.BindingFlags.GetProperty, null, application, null);
                presentation = presentations!.GetType().InvokeMember(
                    "Open",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    presentations,
                    [path, -1, 0, 0]);

                slides = presentation!.GetType().InvokeMember("Slides", System.Reflection.BindingFlags.GetProperty, null, presentation, null);
                var slideCount = Convert.ToInt32(slides!.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, slides, null));
                var slideTexts = new List<string>();

                for (var index = 1; index <= slideCount; index++)
                {
                    object? slide = null;
                    object? shapes = null;

                    try
                    {
                        slide = slides.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, slides, [index]);
                        shapes = slide!.GetType().InvokeMember("Shapes", System.Reflection.BindingFlags.GetProperty, null, slide, null);
                        var shapeCount = Convert.ToInt32(shapes!.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, shapes, null));
                        var textRuns = new List<string>();

                        for (var shapeIndex = 1; shapeIndex <= shapeCount; shapeIndex++)
                        {
                            object? shape = null;
                            object? textFrame = null;
                            object? textRange = null;

                            try
                            {
                                shape = shapes.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, shapes, [shapeIndex]);
                                var hasTextFrame = Convert.ToInt32(shape!.GetType().InvokeMember("HasTextFrame", System.Reflection.BindingFlags.GetProperty, null, shape, null));
                                if (hasTextFrame == 0)
                                {
                                    continue;
                                }

                                textFrame = shape.GetType().InvokeMember("TextFrame", System.Reflection.BindingFlags.GetProperty, null, shape, null);
                                var hasText = Convert.ToInt32(textFrame!.GetType().InvokeMember("HasText", System.Reflection.BindingFlags.GetProperty, null, textFrame, null));
                                if (hasText == 0)
                                {
                                    continue;
                                }

                                textRange = textFrame.GetType().InvokeMember("TextRange", System.Reflection.BindingFlags.GetProperty, null, textFrame, null);
                                var text = textRange?.GetType().InvokeMember("Text", System.Reflection.BindingFlags.GetProperty, null, textRange, null) as string;
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    textRuns.Add(text.Trim());
                                }
                            }
                            finally
                            {
                                ReleaseComObject(textRange);
                                ReleaseComObject(textFrame);
                                ReleaseComObject(shape);
                            }
                        }

                        if (textRuns.Count > 0)
                        {
                            slideTexts.Add($"Slide {index}{Environment.NewLine}{string.Join(Environment.NewLine, textRuns)}");
                        }
                    }
                    finally
                    {
                        ReleaseComObject(shapes);
                        ReleaseComObject(slide);
                    }
                }

                return string.Join(Environment.NewLine + Environment.NewLine, slideTexts);
            }
            finally
            {
                TryInvoke(presentation, "Close");
                TryInvoke(application, "Quit");
                ReleaseComObject(slides);
                ReleaseComObject(presentation);
                ReleaseComObject(presentations);
                ReleaseComObject(application);
            }
        });
    }

    public static string ExtractWordPreview(string path)
    {
        return RunInSta(() =>
        {
            var applicationType = Type.GetTypeFromProgID("Word.Application")
                ?? throw new InvalidOperationException("Microsoft Word is not installed.");

            object? application = null;
            object? documents = null;
            object? document = null;
            object? content = null;

            try
            {
                application = Activator.CreateInstance(applicationType);
                applicationType.InvokeMember("Visible", System.Reflection.BindingFlags.SetProperty, null, application, [false]);
                applicationType.InvokeMember("DisplayAlerts", System.Reflection.BindingFlags.SetProperty, null, application, [0]);

                documents = applicationType.InvokeMember("Documents", System.Reflection.BindingFlags.GetProperty, null, application, null);
                document = documents!.GetType().InvokeMember(
                    "Open",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    documents,
                    [path, Type.Missing, true]);

                content = document!.GetType().InvokeMember("Content", System.Reflection.BindingFlags.GetProperty, null, document, null);
                var text = content?.GetType().InvokeMember("Text", System.Reflection.BindingFlags.GetProperty, null, content, null) as string;
                return NormalizeWordText(text ?? string.Empty);
            }
            finally
            {
                TryInvoke(document, "Close", false);
                TryInvoke(application, "Quit");
                ReleaseComObject(content);
                ReleaseComObject(document);
                ReleaseComObject(documents);
                ReleaseComObject(application);
            }
        });
    }

    public static string ExtractExcelPreview(string path)
    {
        return RunInSta(() =>
        {
            var applicationType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Microsoft Excel is not installed.");

            object? application = null;
            object? workbooks = null;
            object? workbook = null;
            object? worksheets = null;

            try
            {
                application = Activator.CreateInstance(applicationType);
                applicationType.InvokeMember("Visible", System.Reflection.BindingFlags.SetProperty, null, application, [false]);
                applicationType.InvokeMember("DisplayAlerts", System.Reflection.BindingFlags.SetProperty, null, application, [false]);

                workbooks = applicationType.InvokeMember("Workbooks", System.Reflection.BindingFlags.GetProperty, null, application, null);
                workbook = workbooks!.GetType().InvokeMember(
                    "Open",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    workbooks,
                    [path, Type.Missing, true]);

                worksheets = workbook!.GetType().InvokeMember("Worksheets", System.Reflection.BindingFlags.GetProperty, null, workbook, null);
                var worksheetCount = Convert.ToInt32(worksheets!.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, worksheets, null));
                var sheetTexts = new List<string>();

                for (var index = 1; index <= worksheetCount; index++)
                {
                    object? worksheet = null;
                    object? usedRange = null;

                    try
                    {
                        worksheet = worksheets.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, worksheets, [index]);
                        var sheetName = worksheet?.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, worksheet, null) as string
                            ?? $"Sheet {index}";
                        usedRange = worksheet!.GetType().InvokeMember("UsedRange", System.Reflection.BindingFlags.GetProperty, null, worksheet, null);
                        var values = usedRange?.GetType().InvokeMember("Value2", System.Reflection.BindingFlags.GetProperty, null, usedRange, null);
                        var sheetText = FormatWorksheetValues(sheetName, values);
                        if (!string.IsNullOrWhiteSpace(sheetText))
                        {
                            sheetTexts.Add(sheetText);
                        }
                    }
                    finally
                    {
                        ReleaseComObject(usedRange);
                        ReleaseComObject(worksheet);
                    }
                }

                return string.Join(Environment.NewLine + Environment.NewLine, sheetTexts);
            }
            finally
            {
                TryInvoke(workbook, "Close", false);
                TryInvoke(application, "Quit");
                ReleaseComObject(worksheets);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(application);
            }
        });
    }

    private static string FormatWorksheetValues(string sheetName, object? values)
    {
        var rows = new List<string>();

        if (values is object[,] cells)
        {
            var lowerRow = cells.GetLowerBound(0);
            var upperRow = cells.GetUpperBound(0);
            var lowerColumn = cells.GetLowerBound(1);
            var upperColumn = cells.GetUpperBound(1);

            for (var rowIndex = lowerRow; rowIndex <= upperRow; rowIndex++)
            {
                var columns = new List<string>();
                for (var columnIndex = lowerColumn; columnIndex <= upperColumn; columnIndex++)
                {
                    columns.Add(FormatCellValue(cells[rowIndex, columnIndex]));
                }

                if (columns.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(string.Join("\t", columns));
            }
        }
        else if (values is not null)
        {
            rows.Add(FormatCellValue(values));
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        return $"{sheetName}{Environment.NewLine}{string.Join(Environment.NewLine, rows)}";
    }

    private static string FormatCellValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            double number => number.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture),
            float number => number.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture),
            decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool boolean => boolean ? "TRUE" : "FALSE",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string NormalizeWordText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\r' || character == '\a' || character == '\f' || character == '\v')
            {
                continue;
            }

            builder.Append(character);
        }

        var lines = builder
            .ToString()
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static T RunInSta<T>(Func<T> operation)
    {
        T? result = default;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException(capturedException.Message, capturedException);
        }

        return result!;
    }

    private static void TryInvoke(object? instance, string memberName, params object?[] args)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            instance.GetType().InvokeMember(memberName, System.Reflection.BindingFlags.InvokeMethod, null, instance, args);
        }
        catch
        {
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }
}