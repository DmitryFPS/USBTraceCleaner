using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.Report;

public static class PdfReportGenerator
{
    private const string FontName = "Segoe UI";

    static PdfReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void Generate(PdfReportRequest request, string filePath)
    {
        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(32);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(FontName));

                page.Header().Element(c => ComposeHeader(c, request));
                page.Content().PaddingTop(8).Element(c => ComposeContent(c, request));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span("Стр. ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void ComposeHeader(IContainer container, PdfReportRequest request)
    {
        var moduleTitle = request.Module switch
        {
            ReportModule.Usb => "USB — очистка следов",
            _ => "Аудит сети"
        };

        var operationTitle = request.Operation switch
        {
            ReportOperationType.Scan => "Сканирование",
            ReportOperationType.Clean => "Очистка",
            ReportOperationType.GhostClean => "Удаление призраков PnP",
            _ => "Отчёт"
        };

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("USB Trace Cleaner").Bold().FontSize(16).FontColor(Colors.Blue.Darken2);
                    left.Item().Text($"{moduleTitle} · {operationTitle}").SemiBold().FontSize(12);
                });
                row.ConstantItem(180).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(request.GeneratedAt.ToString("dd.MM.yyyy HH:mm:ss"));
                    right.Item().AlignRight().Text($"v{request.AppVersion}").FontColor(Colors.Grey.Darken1);
                });
            });

            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"ПК: {request.ComputerName}");
                row.RelativeItem().AlignRight().Text(request.OsVersion);
            });
        });
    }

    private static void ComposeContent(IContainer container, PdfReportRequest request)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ComposeSummary(c, request));
            if (request.Module == ReportModule.Network && request.Summary.ConnectionSummary.Count > 0)
                column.Item().PaddingTop(12).Element(c => ComposeConnectionSummary(c, request.Summary.ConnectionSummary));
            column.Item().PaddingTop(12).Element(c => ComposeCategoryBreakdown(c, request.Summary));

            if (request.Module == ReportModule.Usb)
                column.Item().PaddingTop(12).Element(c => ComposeUsbTable(c, request));
            else
                column.Item().PaddingTop(12).Element(c => ComposeNetworkTable(c, request));

            column.Item().PaddingTop(12).Element(c => ComposeLog(c, request.LogText));

            if (request.Module == ReportModule.Network && request.Operation == ReportOperationType.Scan)
            {
                column.Item().PaddingTop(8).Text(
                    "Примечание: раздел «Роутер» — устройства в LAN; на диске ПК эти записи не хранятся.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            }
        });
    }

    private static void ComposeSummary(IContainer container, PdfReportRequest request)
    {
        var s = request.Summary;
        container.Background(Colors.Grey.Lighten4).Padding(10).Column(column =>
        {
            column.Item().Text("Сводка").Bold().FontSize(11);

            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn();
                    cols.RelativeColumn();
                });

                void Row(string label, string value)
                {
                    table.Cell().PaddingVertical(2).Text(label).FontColor(Colors.Grey.Darken2);
                    table.Cell().PaddingVertical(2).Text(value).SemiBold();
                }

                Row("Всего записей", s.TotalItems.ToString());
                Row("Отмечено для очистки", s.SelectedItems.ToString());

                if (s.UsbStorageCount.HasValue)
                    Row("USB-накопители", s.UsbStorageCount.Value.ToString());
                if (s.UsbStorRemaining.HasValue)
                    Row("USB-накопителей осталось", s.UsbStorRemaining.Value.ToString());
                if (s.AllowedCount.HasValue)
                    Row("Разрешено (белый список)", s.AllowedCount.Value.ToString());
                if (s.UnknownCount.HasValue)
                    Row("Неизвестных", s.UnknownCount.Value.ToString());
                if (s.CleanableCount.HasValue)
                    Row("Доступно для очистки", s.CleanableCount.Value.ToString());

                if (request.Operation is ReportOperationType.Clean or ReportOperationType.GhostClean)
                {
                    Row("Успешно обработано", s.Processed.ToString());
                    if (s.Skipped > 0)
                        Row("Пропущено (не применимо)", s.Skipped.ToString());
                    Row("Ошибок", s.Failed.ToString());
                }

                if (!string.IsNullOrWhiteSpace(s.PeriodText))
                    Row("Период сканирования", s.PeriodText);
            });

            if (s.SkippedItems.Count > 0)
            {
                column.Item().PaddingTop(6).Text("Пропущено (канал/элемент отсутствует в Windows):").SemiBold().FontColor(Colors.Grey.Darken2);
                foreach (var item in s.SkippedItems.Take(20))
                    column.Item().Text($"• {Truncate(item, 200)}").FontColor(Colors.Grey.Darken1);
            }

            if (s.Failures.Count > 0)
            {
                column.Item().PaddingTop(6).Text("Ошибки (требуют внимания):").SemiBold().FontColor(Colors.Red.Darken1);
                foreach (var fail in s.Failures.Take(20))
                    column.Item().Text($"• {Truncate(fail, 200)}").FontColor(Colors.Red.Darken2);
            }

            if (!string.IsNullOrWhiteSpace(s.OptionsText))
            {
                column.Item().PaddingTop(6).Text("Параметры:").SemiBold();
                column.Item().Text(s.OptionsText).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private static void ComposeConnectionSummary(IContainer container, IReadOnlyList<NetworkAuditSummarySection> sections)
    {
        container.Column(column =>
        {
            column.Item().Text("Подключения — кратко").Bold().FontSize(11);
            foreach (var section in sections)
            {
                column.Item().PaddingTop(8).Text(section.Title).SemiBold().FontSize(10);
                if (!string.IsNullOrWhiteSpace(section.Hint))
                    column.Item().Text(section.Hint!).FontSize(8).FontColor(Colors.Grey.Darken1);
                foreach (var line in section.Lines)
                {
                    var color = section.IsAttention ? Colors.Red.Darken2 : Colors.Grey.Darken3;
                    column.Item().PaddingTop(2).Text($"• {Truncate(line, 240)}").FontSize(9).FontColor(color);
                }
            }
        });
    }

    private static void ComposeCategoryBreakdown(IContainer container, ReportSummary summary)
    {
        if (summary.CategoryCounts.Count == 0) return;

        container.Column(column =>
        {
            column.Item().Text("По категориям").Bold().FontSize(11);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2);
                    cols.ConstantColumn(60);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Blue.Lighten4).Padding(4).Text("Категория").SemiBold();
                    header.Cell().Background(Colors.Blue.Lighten4).Padding(4).AlignRight().Text("Кол-во").SemiBold();
                });

                foreach (var (label, count) in summary.CategoryCounts)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(label);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignRight().Text(count.ToString());
                }
            });
        });
    }

    private static void ComposeUsbTable(IContainer container, PdfReportRequest request)
    {
        container.Column(column =>
        {
            column.Item().Text($"Таблица USB ({request.UsbItems.Count})").Bold().FontSize(11);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(22);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(0.8f);
                    cols.RelativeColumn(2f);
                    cols.RelativeColumn(1.5f);
                });

                table.Header(header =>
                {
                    header.Cell().HeaderCell("✓");
                    header.Cell().HeaderCell("Раздел");
                    header.Cell().HeaderCell("Тип");
                    header.Cell().HeaderCell("Расположение");
                    header.Cell().HeaderCell("Описание");
                });

                foreach (var item in request.UsbItems)
                {
                    table.Cell().BodyCell(item.Selected ? "✓" : "");
                    table.Cell().BodyCell(item.Group);
                    table.Cell().BodyCell(item.Type);
                    table.Cell().BodyCell(Truncate(item.Location, 120));
                    table.Cell().BodyCell(Truncate(item.Description, 100));
                }
            });
        });
    }

    private static void ComposeNetworkTable(IContainer container, PdfReportRequest request)
    {
        container.Column(column =>
        {
            column.Item().Text($"Таблица сети ({request.NetworkItems.Count})").Bold().FontSize(11);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(22);
                    cols.ConstantColumn(52);
                    cols.ConstantColumn(62);
                    cols.RelativeColumn(0.7f);
                    cols.ConstantColumn(44);
                    cols.RelativeColumn(1.2f);
                    cols.RelativeColumn(1f);
                });

                table.Header(header =>
                {
                    header.Cell().HeaderCell("✓");
                    header.Cell().HeaderCell("Статус");
                    header.Cell().HeaderCell("Дата");
                    header.Cell().HeaderCell("Раздел");
                    header.Cell().HeaderCell("Дейст.");
                    header.Cell().HeaderCell("Описание");
                    header.Cell().HeaderCell("Детали");
                });

                foreach (var item in request.NetworkItems)
                {
                    table.Cell().BodyCell(item.Selected ? "✓" : "");
                    table.Cell().BodyCell(item.Authorization);
                    table.Cell().BodyCell(item.Time);
                    table.Cell().BodyCell(item.Group);
                    table.Cell().BodyCell(item.Action);
                    table.Cell().BodyCell(Truncate(item.Title, 80));
                    table.Cell().BodyCell(Truncate(item.Detail, 90));
                }
            });
        });
    }

    private static void ComposeLog(IContainer container, string logText)
    {
        container.Column(column =>
        {
            column.Item().Text("Журнал операции").Bold().FontSize(11);
            column.Item().PaddingTop(4).Background(Colors.Grey.Lighten5).Padding(8)
                .Text(string.IsNullOrWhiteSpace(logText) ? "—" : Truncate(logText, 12000))
                .FontSize(8)
                .LineHeight(1.25f);
        });
    }

    private static void HeaderCell(this IContainer cell, string text) =>
        cell.Background(Colors.Blue.Lighten4).Padding(4).Text(text).SemiBold().FontSize(8);

    private static void BodyCell(this IContainer cell, string text) =>
        cell.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(text ?? "").FontSize(8);

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= max ? text : text[..max] + "…";
    }
}
