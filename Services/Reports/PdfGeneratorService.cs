using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using RegistroCx.Models.ReportModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Previewer;

namespace RegistroCx.Services.Reports;

public class PdfGeneratorService
{
    private readonly string _tempDirectory;
    private readonly ChartGeneratorService _chartGenerator;

    public PdfGeneratorService(ChartGeneratorService chartGenerator)
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RegistroCx_Reports");
        _chartGenerator = chartGenerator;
        
        // Crear directorio temporal si no existe
        if (!Directory.Exists(_tempDirectory))
            Directory.CreateDirectory(_tempDirectory);

        // Configurar QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> CreateWeeklyReportPdfAsync(ReportData reportData, CancellationToken ct = default)
    {
        var fileName = $"ReporteSemanal_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        var filePath = Path.Combine(_tempDirectory, fileName);
        
        await Task.Run(() => 
        {
            CreateReportPdf(filePath, "Reporte Semanal", reportData);
        }, ct);
        
        return filePath;
    }

    public async Task<string> CreateMonthlyReportPdfAsync(ReportData reportData, CancellationToken ct = default)
    {
        var fileName = $"ReporteMensual_{reportData.Period.DisplayName.Replace(" ", "-")}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        var filePath = Path.Combine(_tempDirectory, fileName);
        
        await Task.Run(() => 
        {
            CreateReportPdf(filePath, "Reporte Mensual", reportData);
        }, ct);
        
        return filePath;
    }

    public async Task<string> CreateAnnualReportPdfAsync(ReportData reportData, CancellationToken ct = default)
    {
        var fileName = $"ReporteAnual_{reportData.Period.DisplayName.Replace(" ", "-")}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        var filePath = Path.Combine(_tempDirectory, fileName);
        
        await Task.Run(() => 
        {
            CreateReportPdf(filePath, "Reporte Anual", reportData);
        }, ct);
        
        return filePath;
    }

    private void CreateReportPdf(string filePath, string reportTitle, ReportData reportData)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(container => CreateHeader(container, reportTitle, reportData));
                page.Content().Element(container => CreateContent(container, reportData));
                page.Footer().Element(CreateFooter);
            });
        })
        .GeneratePdf(filePath);
    }

    private void CreateHeader(IContainer container, string title, ReportData reportData)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(title)
                    .FontSize(24).Bold().FontColor(Colors.Blue.Darken2);
                
                column.Item().Text(reportData.Period.DisplayName)
                    .FontSize(16).SemiBold().FontColor(Colors.Grey.Darken1);
                
                column.Item().Text($"{reportData.Period.StartDate:dd/MM/yyyy} - {reportData.Period.EndDate:dd/MM/yyyy}")
                    .FontSize(12).FontColor(Colors.Grey.Medium);
            });

            row.ConstantItem(100).Height(50).Placeholder();
        });
    }

    private void CreateContent(IContainer container, ReportData reportData)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // Resumen ejecutivo
            column.Item().Element(container => CreateExecutiveSummary(container, reportData));

            // Gráficos visuales
            column.Item().Element(container => CreateChartsSection(container, reportData));

            // Estadísticas de cirujanos
            column.Item().Element(container => CreateSurgeonStatistics(container, reportData));

            // Cirugías por tipo
            column.Item().Element(container => CreateSurgeryTypeBreakdown(container, reportData));

            // Top colaboraciones
            column.Item().Element(container => CreateTopCollaborations(container, reportData));

            // Métricas adicionales
            column.Item().Element(container => CreateAdditionalMetrics(container, reportData));
        });
    }

    private void CreateExecutiveSummary(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("📊 RESUMEN EJECUTIVO")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Total de Cirugías").FontSize(12).SemiBold();
                        col.Item().Text($"{reportData.TotalSurgeries}").FontSize(24).Bold().FontColor(Colors.Green.Darken2);
                    });
                });

                row.RelativeItem().PaddingLeft(10).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Promedio Diario").FontSize(12).SemiBold();
                        col.Item().Text($"{reportData.AverageSurgeriesPerDay:F1}").FontSize(24).Bold().FontColor(Colors.Orange.Darken2);
                    });
                });

                row.RelativeItem().PaddingLeft(10).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Cirujanos Activos").FontSize(12).SemiBold();
                        col.Item().Text($"{reportData.SurgeonStats.Count}").FontSize(24).Bold().FontColor(Colors.Purple.Darken2);
                    });
                });
            });
        });
    }

    private void CreateChartsSection(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("📈 ANÁLISIS VISUAL")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            column.Item().PaddingTop(10).Row(row =>
            {
                // Gráfico de tipos de cirugía
                if (reportData.SurgeriesByType.Any())
                {
                    row.RelativeItem().Element(container =>
                    {
                        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
                        {
                            col.Item().Text("Cirugías por Tipo").FontSize(12).SemiBold().AlignCenter();
                            
                            try
                            {
                                var chartBytes = _chartGenerator.GenerateSurgeryTypeChart(reportData.SurgeriesByType, 200, 150);
                                col.Item().Height(150).Image(chartBytes);
                            }
                            catch
                            {
                                col.Item().Height(150).AlignCenter().Text("Gráfico no disponible").FontColor(Colors.Grey.Medium);
                            }
                        });
                    });
                }

                row.ConstantItem(10); // Espaciado

                // Gráfico de volumen por cirujano
                if (reportData.SurgeonStats.Any())
                {
                    row.RelativeItem().Element(container =>
                    {
                        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
                        {
                            col.Item().Text("Top Cirujanos").FontSize(12).SemiBold().AlignCenter();
                            
                            try
                            {
                                var chartBytes = _chartGenerator.GenerateSurgeonVolumeChart(reportData.SurgeonStats, 200, 150);
                                col.Item().Height(150).Image(chartBytes);
                            }
                            catch
                            {
                                col.Item().Height(150).AlignCenter().Text("Gráfico no disponible").FontColor(Colors.Grey.Medium);
                            }
                        });
                    });
                }
            });

            // Gráfico de tendencia temporal (si hay datos temporales)
            if (reportData.DailySurgeriesTimeline.Any())
            {
                column.Item().PaddingTop(15).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Tendencia Temporal").FontSize(12).SemiBold().AlignCenter();
                        
                        try
                        {
                            var chartBytes = _chartGenerator.GenerateTimelineChart(reportData.DailySurgeriesTimeline, 450, 200);
                            col.Item().Height(200).AlignCenter().Image(chartBytes);
                        }
                        catch
                        {
                            col.Item().Height(200).AlignCenter().Text("Gráfico de tendencia no disponible").FontColor(Colors.Grey.Medium);
                        }
                    });
                });
            }
        });
    }

    private void CreateSurgeonStatistics(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("👨‍⚕️ ESTADÍSTICAS DE CIRUJANOS")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            if (reportData.SurgeonStats.Any())
            {
                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyle).Text("Cirujano").Bold();
                        header.Cell().Element(CellStyle).Text("Total").Bold();
                        header.Cell().Element(CellStyle).Text("Especialización").Bold();
                        header.Cell().Element(CellStyle).Text("Centros").Bold();
                        header.Cell().Element(CellStyle).Text("Anestesiólogos").Bold();
                    });

                    foreach (var surgeon in reportData.SurgeonStats.Take(10))
                    {
                        table.Cell().Element(CellStyle).Text(surgeon.SurgeonName);
                        table.Cell().Element(CellStyle).Text(surgeon.TotalSurgeries.ToString());
                        table.Cell().Element(CellStyle).Text($"{surgeon.TopSurgeryType} ({surgeon.TopSurgeryTypeCount})");
                        table.Cell().Element(CellStyle).Text(surgeon.UniqueCenters.ToString());
                        table.Cell().Element(CellStyle).Text(surgeon.UniqueAnesthesiologists.ToString());
                    }
                });
            }
            else
            {
                column.Item().PaddingTop(10).Text("No hay datos de cirujanos disponibles.")
                    .Italic().FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void CreateSurgeryTypeBreakdown(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("🏥 CIRUGÍAS POR TIPO")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            if (reportData.SurgeriesByType.Any())
            {
                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyle).Text("Tipo de Cirugía").Bold();
                        header.Cell().Element(CellStyle).Text("Cantidad").Bold();
                        header.Cell().Element(CellStyle).Text("Porcentaje").Bold();
                    });

                    foreach (var surgeryType in reportData.SurgeriesByType.OrderByDescending(x => x.Value).Take(10))
                    {
                        var percentage = (surgeryType.Value / (double)reportData.TotalSurgeries) * 100;
                        
                        table.Cell().Element(CellStyle).Text(surgeryType.Key);
                        table.Cell().Element(CellStyle).Text(surgeryType.Value.ToString());
                        table.Cell().Element(CellStyle).Text($"{percentage:F1}%");
                    }
                });
            }
        });
    }

    private void CreateTopCollaborations(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("🤝 TOP COLABORACIONES")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            if (reportData.TopCollaborations.Any())
            {
                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyle).Text("Colaboración").Bold();
                        header.Cell().Element(CellStyle).Text("Cirugías").Bold();
                    });

                    foreach (var collaboration in reportData.TopCollaborations.Take(8))
                    {
                        table.Cell().Element(CellStyle).Text(collaboration.DisplayName);
                        table.Cell().Element(CellStyle).Text(collaboration.CollaborationCount.ToString());
                    }
                });
            }
        });
    }

    private void CreateAdditionalMetrics(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("📈 MÉTRICAS ADICIONALES")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("🏢 Centro más utilizado").SemiBold();
                    col.Item().Text(reportData.MostFrequentCenter ?? "N/A");
                    col.Item().PaddingBottom(10);

                    col.Item().Text("💉 Anestesiólogo principal").SemiBold();
                    col.Item().Text(reportData.MostFrequentAnesthesiologist ?? "N/A");
                    col.Item().PaddingBottom(10);
                });

                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("📅 Día más activo").SemiBold();
                    col.Item().Text(GetDayName(reportData.MostActiveDay));
                    col.Item().PaddingBottom(10);

                    col.Item().Text("⏰ Horario preferido").SemiBold();
                    col.Item().Text($"{reportData.MostActiveHour:D2}:00");
                    col.Item().PaddingBottom(10);
                });
            });
        });
    }

    private void CreateFooter(IContainer container)
    {
        container.AlignCenter().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm} - RegistroCx")
            .FontSize(8).FontColor(Colors.Grey.Medium);
    }

    private static IContainer CellStyle(IContainer container)
    {
        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
    }

    private static string GetDayName(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "Lunes",
            DayOfWeek.Tuesday => "Martes",
            DayOfWeek.Wednesday => "Miércoles",
            DayOfWeek.Thursday => "Jueves",
            DayOfWeek.Friday => "Viernes",
            DayOfWeek.Saturday => "Sábado",
            DayOfWeek.Sunday => "Domingo",
            _ => dayOfWeek.ToString()
        };
    }

    public void CleanupOldReports(TimeSpan maxAge)
    {
        try
        {
            if (!Directory.Exists(_tempDirectory))
                return;

            var cutoffTime = DateTime.Now - maxAge;
            var files = Directory.GetFiles(_tempDirectory, "*.pdf");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffTime)
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        Console.WriteLine($"[PDF] Cleaned up old report: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PDF] Error deleting file {file}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PDF] Error during cleanup: {ex.Message}");
        }
    }
}