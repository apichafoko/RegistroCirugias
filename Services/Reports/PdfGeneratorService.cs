using System;
using System.IO;
using System.Reflection;
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
        
        await CreateReportPdfAsync(filePath, "Reporte Semanal", reportData, ct);
        
        return filePath;
    }

    public async Task<string> CreateMonthlyReportPdfAsync(ReportData reportData, CancellationToken ct = default)
    {
        var fileName = $"ReporteMensual_{reportData.Period.DisplayName.Replace(" ", "-")}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        var filePath = Path.Combine(_tempDirectory, fileName);
        
        await CreateReportPdfAsync(filePath, "Reporte Mensual", reportData, ct);
        
        return filePath;
    }

    public async Task<string> CreateAnnualReportPdfAsync(ReportData reportData, CancellationToken ct = default)
    {
        var fileName = $"ReporteAnual_{reportData.Period.DisplayName.Replace(" ", "-")}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf";
        var filePath = Path.Combine(_tempDirectory, fileName);
        
        await CreateReportPdfAsync(filePath, "Reporte Anual", reportData, ct);
        
        return filePath;
    }

    private async Task CreateReportPdfAsync(string filePath, string reportTitle, ReportData reportData, CancellationToken ct = default)
    {
        // Pre-generar todos los gráficos de forma asíncrona
        byte[]? surgeryTypeChart = null;
        byte[]? surgeonVolumeChart = null;
        byte[]? timelineChart = null;
        byte[]? heatmapChart = null;
        
        try
        {
            if (reportData.SurgeriesByType.Any())
                surgeryTypeChart = await _chartGenerator.GenerateSurgeryTypeChart(reportData.SurgeriesByType, 500, 250);
        }
        catch { /* Fallback: sin gráfico */ }
        
        try
        {
            if (reportData.SurgeonStats.Any())
                surgeonVolumeChart = await _chartGenerator.GenerateSurgeonVolumeChart(reportData.SurgeonStats, 500, 250);
        }
        catch { /* Fallback: sin gráfico */ }
        
        try
        {
            if (reportData.DailySurgeriesTimeline.Any())
            {
                var timelineStringData = reportData.DailySurgeriesTimeline
                    .ToDictionary(x => x.Key.ToString("dd/MM"), x => x.Value);
                timelineChart = await _chartGenerator.GenerateTimelineChart(timelineStringData, 700, 300);
            }
        }
        catch { /* Fallback: sin gráfico */ }
        
        try
        {
            if (reportData.HeatmapData.Any())
                heatmapChart = await _chartGenerator.GenerateHeatmapChart(reportData.HeatmapData, 600, 400);
        }
        catch { /* Fallback: sin gráfico */ }

        Document.Create(container =>
        {
            // Página 1: Resumen ejecutivo
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(container => CreateHeader(container, reportTitle, reportData));
                page.Content().Element(container => CreateSummaryContent(container, reportData));
                page.Footer().Element(CreateFooter);
            });
            
            // Página 2: Gráfico Cirugías por Tipo
            if (surgeryTypeChart != null)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(container => CreateHeader(container, reportTitle + " - Cirugías por Tipo", reportData));
                    page.Content().Element(container => CreateSingleChart(container, "🥧 Cirugías por Tipo", surgeryTypeChart));
                    page.Footer().Element(CreateFooter);
                });
            }
            
            // Página 3: Top Cirujanos
            if (surgeonVolumeChart != null)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(container => CreateHeader(container, reportTitle + " - Top Cirujanos", reportData));
                    page.Content().Element(container => CreateSingleChart(container, "👨‍⚕️ Top Cirujanos por Volumen", surgeonVolumeChart));
                    page.Footer().Element(CreateFooter);
                });
            }
            
            // Página 4: Evolución Temporal
            if (timelineChart != null)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(container => CreateHeader(container, reportTitle + " - Evolución Temporal", reportData));
                    page.Content().Element(container => CreateSingleChart(container, "📈 Evolución en el Tiempo", timelineChart));
                    page.Footer().Element(CreateFooter);
                });
            }
            
            // Página 5: Mapa de Calor (Tabla Visual)
            if (reportData.HeatmapData.Any())
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Element(container => CreateHeader(container, reportTitle + " - Mapa de Calor", reportData));
                    page.Content().Element(container => CreateHeatmapTable(container, reportData));
                    page.Footer().Element(CreateFooter);
                });
            }
            
            // Página Final: Estadísticas detalladas
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(container => CreateHeader(container, reportTitle + " - Estadísticas Detalladas", reportData));
                page.Content().Element(container => CreateDetailedStats(container, reportData));
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

            row.ConstantItem(100).Height(50).AlignCenter().Element(container =>
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var resourceName = "RegistroCx.Images.Logo_Registrocx.png";
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        var logoBytes = ms.ToArray();
                        container.Image(logoBytes).FitArea();
                    }
                    else
                    {
                        container.Text("📋").FontSize(24).FontColor(Colors.Blue.Darken2).AlignCenter();
                    }
                }
                catch
                {
                    container.Text("📋").FontSize(24).FontColor(Colors.Blue.Darken2).AlignCenter();
                }
            });
        });
    }

    private void CreateSummaryContent(IContainer container, ReportData reportData)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // Resumen ejecutivo
            column.Item().Element(container => CreateExecutiveSummary(container, reportData));
            
            // Tendencias comparativas
            if (reportData.PreviousPeriodTotal.HasValue)
            {
                column.Item().Element(container => CreateComparativeTrends(container, reportData));
            }

            // Métricas adicionales
            column.Item().Element(container => CreateAdditionalMetrics(container, reportData));
        });
    }

    private void CreateSingleChart(IContainer container, string chartTitle, byte[] chartBytes)
    {
        container.PaddingVertical(2, Unit.Centimetre).Column(column =>
        {
            column.Item().Text(chartTitle)
                .FontSize(18).Bold().FontColor(Colors.Blue.Darken1).AlignCenter();
                
            column.Item().PaddingTop(30).AlignCenter().Image(chartBytes).FitArea();
        });
    }

    private void CreateHeatmapTable(IContainer container, ReportData reportData)
    {
        container.PaddingVertical(2, Unit.Centimetre).Column(column =>
        {
            column.Item().Text("🔥 Mapa de Calor: Actividad por Día y Hora")
                .FontSize(18).Bold().FontColor(Colors.Blue.Darken1).AlignCenter();
                
            column.Item().PaddingTop(20).Table(table =>
            {
                // 8 columnas: Hora + 7 días
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60); // Hora
                    for (int i = 0; i < 7; i++)
                        columns.RelativeColumn(); // Días
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Hora").Bold().AlignCenter();
                    var dayNames = new[] { "Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };
                    foreach (var day in dayNames)
                    {
                        header.Cell().Element(CellStyle).Text(day).Bold().AlignCenter();
                    }
                });

                // Filas por hora (6AM a 10PM)
                var maxCount = reportData.HeatmapData.Values.SelectMany(d => d.Values).DefaultIfEmpty(0).Max();
                
                for (int hour = 6; hour <= 22; hour++)
                {
                    table.Cell().Element(CellStyle).Text($"{hour:D2}:00").Bold().AlignCenter();
                    
                    for (int day = 0; day <= 6; day++)
                    {
                        var dayOfWeek = (DayOfWeek)day;
                        var count = 0;
                        
                        if (reportData.HeatmapData.ContainsKey(dayOfWeek) && reportData.HeatmapData[dayOfWeek].ContainsKey(hour))
                        {
                            count = reportData.HeatmapData[dayOfWeek][hour];
                        }
                        
                        var intensity = maxCount > 0 ? (double)count / maxCount : 0;
                        var bgColor = count == 0 ? Colors.Grey.Lighten4 : 
                                     intensity <= 0.25 ? Colors.Red.Lighten4 :
                                     intensity <= 0.5 ? Colors.Red.Lighten2 :
                                     intensity <= 0.75 ? Colors.Red.Medium : Colors.Red.Darken2;
                        
                        var textColor = intensity > 0.5 ? Colors.White : Colors.Black;
                        var displayText = count == 0 ? "-" : count.ToString();
                        
                        table.Cell().Element(container => 
                            container.Background(bgColor).Padding(8).Text(displayText)
                                .FontColor(textColor).Bold().AlignCenter()
                        );
                    }
                }
            });
            
            column.Item().PaddingTop(10).Text("Colores: Gris = Sin actividad, Rosa claro a Rojo oscuro = Mayor actividad")
                .FontSize(10).Italic().FontColor(Colors.Grey.Medium).AlignCenter();
        });
    }

    private void CreateDetailedStats(IContainer container, ReportData reportData)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // Estadísticas detalladas de cirujanos
            column.Item().Element(container => CreateSurgeonStatistics(container, reportData));

            // Cirugías por tipo
            column.Item().Element(container => CreateSurgeryTypeBreakdown(container, reportData));

            // Top colaboraciones
            column.Item().Element(container => CreateTopCollaborations(container, reportData));
        });
    }

    private void CreateMainContent(IContainer container, ReportData reportData, byte[]? surgeryTypeChart = null, byte[]? timelineChart = null, byte[]? heatmapChart = null)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // Resumen ejecutivo
            column.Item().Element(container => CreateExecutiveSummary(container, reportData));
            
            // Tendencias comparativas
            if (reportData.PreviousPeriodTotal.HasValue)
            {
                column.Item().Element(container => CreateComparativeTrends(container, reportData));
            }

            // Gráficos principales (sin Top Cirujanos)
            column.Item().Element(container => CreateMainChartsSection(container, reportData, surgeryTypeChart, timelineChart, heatmapChart));
        });
    }

    private void CreateDetailedContent(IContainer container, ReportData reportData, byte[]? surgeonVolumeChart = null)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            // Gráfico de Top Cirujanos (página completa)
            if (reportData.SurgeonStats.Any())
            {
                column.Item().Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("👨‍⚕️ Top Cirujanos por Volumen").FontSize(16).SemiBold().AlignCenter();
                        
                        if (surgeonVolumeChart != null)
                        {
                            col.Item().PaddingTop(15).MaxHeight(400).AlignCenter().Image(surgeonVolumeChart).FitArea();
                        }
                        else
                        {
                            col.Item().Height(300).AlignCenter().Text("Gráfico de cirujanos no disponible").FontColor(Colors.Grey.Medium);
                        }
                    });
                });
            }

            // Estadísticas detalladas de cirujanos
            column.Item().Element(container => CreateSurgeonStatistics(container, reportData));

            // Cirugías por tipo
            column.Item().Element(container => CreateSurgeryTypeBreakdown(container, reportData));

            // Top colaboraciones
            column.Item().Element(container => CreateTopCollaborations(container, reportData));
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

    private void CreateMainChartsSection(IContainer container, ReportData reportData, byte[]? surgeryTypeChart = null, byte[]? timelineChart = null, byte[]? heatmapChart = null)
    {
        container.Column(column =>
        {
            column.Item().Text("📈 ANÁLISIS VISUAL")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            // Gráfico de cirugías por tipo (ancho completo)
            if (reportData.SurgeriesByType.Any())
            {
                column.Item().PaddingTop(15).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Cirugías por Tipo").FontSize(14).SemiBold().AlignCenter();
                        
                        if (surgeryTypeChart != null)
                        {
                            col.Item().MaxHeight(250).AlignCenter().Image(surgeryTypeChart).FitArea();
                        }
                        else
                        {
                            col.Item().Height(200).AlignCenter().Text("Gráfico de cirugías por tipo no disponible").FontColor(Colors.Grey.Medium);
                        }
                    });
                });
            }
            
            // Gráfico de evolución temporal
            if (reportData.DailySurgeriesTimeline.Any())
            {
                column.Item().PaddingTop(15).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("Evolución Temporal").FontSize(14).SemiBold().AlignCenter();
                        
                        if (timelineChart != null)
                        {
                            col.Item().MaxHeight(300).AlignCenter().Image(timelineChart).FitArea();
                        }
                        else
                        {
                            col.Item().Height(200).AlignCenter().Text("Gráfico de evolución temporal no disponible").FontColor(Colors.Grey.Medium);
                        }
                    });
                });
            }
            
            // Mapa de calor horario
            if (reportData.HeatmapData.Any())
            {
                column.Item().PaddingTop(15).Element(container =>
                {
                    container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(col =>
                    {
                        col.Item().Text("🔥 Mapa de Calor: Actividad por Día y Hora").FontSize(14).SemiBold().AlignCenter();
                        
                        if (heatmapChart != null)
                        {
                            col.Item().MaxHeight(400).AlignCenter().Image(heatmapChart).FitArea();
                        }
                        else
                        {
                            col.Item().Height(200).AlignCenter().Text("Mapa de calor no disponible").FontColor(Colors.Grey.Medium);
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

    private void CreateComparativeTrends(IContainer container, ReportData reportData)
    {
        container.Column(column =>
        {
            column.Item().Text("📊 COMPARACIÓN CON PERÍODO ANTERIOR")
                .FontSize(16).Bold().FontColor(Colors.Blue.Darken1);

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("📈 Período Actual").SemiBold();
                    col.Item().Text($"{reportData.TotalSurgeries} cirugías").FontSize(20).Bold().FontColor(Colors.Blue.Darken2);
                });

                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("📅 Período Anterior").SemiBold();
                    col.Item().Text($"{reportData.PreviousPeriodTotal} cirugías").FontSize(20).Bold().FontColor(Colors.Grey.Darken1);
                });

                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("🔄 Cambio").SemiBold();
                    var changeColor = reportData.PercentageChange > 0 ? Colors.Green.Darken1 : 
                                     reportData.PercentageChange < 0 ? Colors.Red.Darken1 : Colors.Grey.Darken1;
                    col.Item().Text($"{reportData.ChangeDirection} {Math.Abs(reportData.PercentageChange):F1}%")
                        .FontSize(20).Bold().FontColor(changeColor);
                });
            });
        });
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