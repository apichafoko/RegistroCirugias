using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Web;
using RegistroCx.Models.ReportModels;

namespace RegistroCx.Services.Reports;

public class ChartGeneratorService
{
    private readonly HttpClient _httpClient;
    private const string QuickChartBaseUrl = "https://quickchart.io/chart";

    public ChartGeneratorService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<byte[]> GenerateSurgeryTypeChart(Dictionary<string, int> surgeryTypeData, int width = 400, int height = 300)
    {
        try
        {
            var topSurgeries = surgeryTypeData.OrderByDescending(x => x.Value).Take(6).ToList();
            if (!topSurgeries.Any()) return await GenerateNoDataChart(width, height);

            var labels = topSurgeries.Select(x => x.Key).ToArray();
            var data = topSurgeries.Select(x => x.Value).ToArray();
            var colors = new[] { "#4A90E2", "#50C878", "#FF6B6B", "#FFD93D", "#9B59B6", "#FFA500" };

            var chartConfig = new
            {
                type = "pie",
                data = new
                {
                    labels = labels,
                    datasets = new[]
                    {
                        new
                        {
                            data = data,
                            backgroundColor = colors.Take(labels.Length).ToArray()
                        }
                    }
                },
                options = new
                {
                    title = new
                    {
                        display = true,
                        text = "Cirugías por Tipo"
                    },
                    plugins = new
                    {
                        legend = new
                        {
                            position = "right"
                        }
                    }
                }
            };

            return await GenerateChartFromConfig(chartConfig, width, height);
        }
        catch
        {
            return await GenerateNoDataChart(width, height);
        }
    }

    public async Task<byte[]> GenerateSurgeonVolumeChart(List<SurgeonStatistics> surgeonStats, int width = 500, int height = 250)
    {
        try
        {
            var topSurgeons = surgeonStats.OrderByDescending(x => x.TotalSurgeries).Take(8).ToList();
            if (!topSurgeons.Any()) return await GenerateNoDataChart(width, height);

            var labels = topSurgeons.Select(x => x.SurgeonName).ToArray();
            var data = topSurgeons.Select(x => x.TotalSurgeries).ToArray();

            var chartConfig = new
            {
                type = "bar",
                data = new
                {
                    labels = labels,
                    datasets = new[]
                    {
                        new
                        {
                            label = "Cirugías",
                            data = data,
                            backgroundColor = "#4A90E2"
                        }
                    }
                },
                options = new
                {
                    title = new
                    {
                        display = true,
                        text = "Top Cirujanos por Volumen"
                    },
                    scales = new
                    {
                        yAxes = new[]
                        {
                            new
                            {
                                ticks = new
                                {
                                    beginAtZero = true,
                                    min = 0
                                }
                            }
                        }
                    },
                    maintainAspectRatio = false
                }
            };

            return await GenerateChartFromConfig(chartConfig, width, height);
        }
        catch
        {
            return await GenerateNoDataChart(width, height);
        }
    }

    public async Task<byte[]> GenerateHeatmapChart(Dictionary<DayOfWeek, Dictionary<int, int>> heatmapData, int width = 600, int height = 400)
    {
        try
        {
            Console.WriteLine($"[HEATMAP-CHART] Creating heatmap table with {heatmapData.Count} days");
            
            var dayNames = new[] { "Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };
            var maxCount = heatmapData.Values.SelectMany(d => d.Values).DefaultIfEmpty(0).Max();
            Console.WriteLine($"[HEATMAP-CHART] Max surgery count: {maxCount}");
            
            // Crear tabla HTML para el heatmap
            var htmlTable = "<table style='border-collapse: collapse; width: 100%; font-family: Arial;'>";
            
            // Header con días
            htmlTable += "<tr><th style='border: 1px solid #ddd; padding: 8px; background: #f5f5f5;'>Hora</th>";
            foreach (var day in dayNames)
            {
                htmlTable += $"<th style='border: 1px solid #ddd; padding: 8px; background: #f5f5f5; text-align: center;'>{day}</th>";
            }
            htmlTable += "</tr>";
            
            // Filas por hora
            for (int hour = 6; hour <= 22; hour++)
            {
                htmlTable += $"<tr><td style='border: 1px solid #ddd; padding: 8px; font-weight: bold;'>{hour:D2}:00</td>";
                
                for (int day = 0; day <= 6; day++)
                {
                    var dayOfWeek = (DayOfWeek)day;
                    var count = 0;
                    
                    if (heatmapData.ContainsKey(dayOfWeek) && heatmapData[dayOfWeek].ContainsKey(hour))
                    {
                        count = heatmapData[dayOfWeek][hour];
                    }
                    
                    var intensity = maxCount > 0 ? (double)count / maxCount : 0;
                    var bgColor = count == 0 ? "#f9f9f9" : 
                                 intensity <= 0.25 ? "#ffebee" :
                                 intensity <= 0.5 ? "#ffcdd2" :
                                 intensity <= 0.75 ? "#e57373" : "#d32f2f";
                    
                    var textColor = intensity > 0.5 ? "white" : "black";
                    var displayText = count == 0 ? "-" : count.ToString();
                    
                    htmlTable += $"<td style='border: 1px solid #ddd; padding: 8px; text-align: center; background-color: {bgColor}; color: {textColor}; font-weight: bold;'>{displayText}</td>";
                }
                htmlTable += "</tr>";
            }
            htmlTable += "</table>";
            
            // Crear gráfico simple que muestre la tabla
            var chartConfig = new
            {
                type = "bar",
                data = new
                {
                    labels = new[] { "Ver tabla en PDF" },
                    datasets = new[]
                    {
                        new
                        {
                            data = new[] { 1 },
                            backgroundColor = "#4A90E2"
                        }
                    }
                },
                options = new
                {
                    title = new
                    {
                        display = true,
                        text = "🔥 Mapa de Calor disponible en PDF"
                    },
                    legend = new { display = false },
                    scales = new
                    {
                        yAxes = new[]
                        {
                            new
                            {
                                ticks = new { display = false }
                            }
                        }
                    }
                }
            };

            Console.WriteLine($"[HEATMAP-CHART] Generated placeholder chart, actual heatmap will be in PDF table");
            return await GenerateChartFromConfig(chartConfig, width, height);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HEATMAP-CHART] Error: {ex.Message}");
            return await GenerateNoDataChart(width, height);
        }
    }

    public async Task<byte[]> GenerateTimelineChart(Dictionary<string, int> timelineData, int width = 700, int height = 300)
    {
        try
        {
            if (!timelineData.Any()) return await GenerateNoDataChart(width, height);

            var sortedData = timelineData.OrderBy(x => x.Key).ToList();
            var labels = sortedData.Select(x => x.Key).ToArray();
            var data = sortedData.Select(x => x.Value).ToArray();

            var chartConfig = new
            {
                type = "line",
                data = new
                {
                    labels = labels,
                    datasets = new[]
                    {
                        new
                        {
                            label = "Cirugías",
                            data = data,
                            borderColor = "#4A90E2",
                            backgroundColor = "rgba(74, 144, 226, 0.1)",
                            fill = true,
                            tension = 0.3
                        }
                    }
                },
                options = new
                {
                    title = new
                    {
                        display = true,
                        text = "Evolución Temporal"
                    },
                    scales = new
                    {
                        yAxes = new[]
                        {
                            new
                            {
                                ticks = new
                                {
                                    beginAtZero = true,
                                    min = 0
                                }
                            }
                        }
                    }
                }
            };

            return await GenerateChartFromConfig(chartConfig, width, height);
        }
        catch
        {
            return await GenerateNoDataChart(width, height);
        }
    }

    private async Task<byte[]> GenerateChartFromConfig(object config, int width, int height)
    {
        var configJson = System.Text.Json.JsonSerializer.Serialize(config);
        var encodedConfig = HttpUtility.UrlEncode(configJson);
        
        var url = $"{QuickChartBaseUrl}?c={encodedConfig}&w={width}&h={height}&format=png";
        
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<byte[]> GenerateNoDataChart(int width, int height)
    {
        var chartConfig = new
        {
            type = "bar",
            data = new
            {
                labels = new[] { "Sin datos" },
                datasets = new[]
                {
                    new
                    {
                        data = new[] { 0 },
                        backgroundColor = "#E0E0E0"
                    }
                }
            },
            options = new
            {
                title = new
                {
                    display = true,
                    text = "No hay datos disponibles"
                }
            }
        };

        return await GenerateChartFromConfig(chartConfig, width, height);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}