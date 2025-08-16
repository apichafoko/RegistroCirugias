using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using RegistroCx.Models.ReportModels;

namespace RegistroCx.Services.Reports;

public class ChartGeneratorService
{
    public byte[] GenerateSurgeryTypeChart(Dictionary<string, int> surgeryTypeData, int width = 400, int height = 300)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        
        canvas.Clear(SKColors.White);
        
        // Preparar datos para gráfico de torta
        var topSurgeries = surgeryTypeData.OrderByDescending(x => x.Value).Take(6).ToList();
        var totalValue = topSurgeries.Sum(x => x.Value);
        
        if (totalValue == 0) return GenerateNoDataChart(width, height);
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 16);
        var titleBounds = new SKRect();
        titleFont.MeasureText("Cirugías por Tipo", out titleBounds);
        canvas.DrawText("Cirugías por Tipo", (width - titleBounds.Width) / 2, 25, SKTextAlign.Left, titleFont, titlePaint);
        
        // Configurar área del gráfico de torta
        var chartSize = Math.Min(width - 160, height - 80); // Dejar espacio para leyenda
        var centerX = chartSize / 2 + 20;
        var centerY = height / 2;
        var radius = chartSize / 2 - 10;
        
        // Colores para las secciones
        var colors = new[]
        {
            SKColor.Parse("#4A90E2"), // Azul
            SKColor.Parse("#50C878"), // Verde
            SKColor.Parse("#FF6B6B"), // Rojo
            SKColor.Parse("#FFD93D"), // Amarillo
            SKColor.Parse("#9B59B6"), // Púrpura
            SKColor.Parse("#FFA500")  // Naranja
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var textFont = new SKFont(SKTypeface.Default, 12);
        
        // Dibujar secciones de la torta
        float currentAngle = -90; // Empezar arriba
        
        for (int i = 0; i < topSurgeries.Count; i++)
        {
            var surgery = topSurgeries[i];
            var percentage = (surgery.Value / (float)totalValue) * 100;
            var sweepAngle = (surgery.Value / (float)totalValue) * 360;
            
            using var sectionPaint = new SKPaint
            {
                Color = colors[i % colors.Length],
                IsAntialias = true
            };
            
            // Dibujar sección de la torta
            var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
            canvas.DrawArc(rect, currentAngle, sweepAngle, true, sectionPaint);
            
            // Dibujar leyenda
            var legendY = 50 + i * 25;
            var legendRect = new SKRect(centerX + radius + 20, legendY - 8, centerX + radius + 35, legendY + 8);
            canvas.DrawRect(legendRect, sectionPaint);
            
            var legendText = $"{surgery.Key}: {surgery.Value} ({percentage:F1}%)";
            if (legendText.Length > 30) legendText = legendText.Substring(0, 27) + "...";
            canvas.DrawText(legendText, centerX + radius + 45, legendY + 4, SKTextAlign.Left, textFont, textPaint);
            
            currentAngle += sweepAngle;
        }
        
        // Obtener imagen como bytes
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    
    public byte[] GenerateSurgeonVolumeChart(List<SurgeonStatistics> surgeonStats, int width = 400, int height = 300)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        
        canvas.Clear(SKColors.White);
        
        // Tomar top 6 cirujanos
        var topSurgeons = surgeonStats.OrderByDescending(x => x.TotalSurgeries).Take(6).ToList();
        if (!topSurgeons.Any()) return GenerateNoDataChart(width, height);
        
        var totalSurgeries = topSurgeons.Sum(x => x.TotalSurgeries);
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 16);
        var titleBounds = new SKRect();
        titleFont.MeasureText("Top Cirujanos por Volumen", out titleBounds);
        canvas.DrawText("Top Cirujanos por Volumen", (width - titleBounds.Width) / 2, 25, SKTextAlign.Left, titleFont, titlePaint);
        
        // Configurar área del gráfico de torta
        var chartSize = Math.Min(width - 160, height - 80); // Dejar espacio para leyenda
        var centerX = chartSize / 2 + 20;
        var centerY = height / 2;
        var radius = chartSize / 2 - 10;
        
        // Colores para las secciones
        var colors = new[]
        {
            SKColor.Parse("#28A745"), // Verde
            SKColor.Parse("#007BFF"), // Azul
            SKColor.Parse("#FFC107"), // Amarillo
            SKColor.Parse("#DC3545"), // Rojo
            SKColor.Parse("#6F42C1"), // Púrpura
            SKColor.Parse("#20C997")  // Turquesa
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var textFont = new SKFont(SKTypeface.Default, 11);
        
        // Dibujar secciones de la torta
        float currentAngle = -90; // Empezar arriba
        
        for (int i = 0; i < topSurgeons.Count; i++)
        {
            var surgeon = topSurgeons[i];
            var percentage = (surgeon.TotalSurgeries / (float)totalSurgeries) * 100;
            var sweepAngle = (surgeon.TotalSurgeries / (float)totalSurgeries) * 360;
            
            using var sectionPaint = new SKPaint
            {
                Color = colors[i % colors.Length],
                IsAntialias = true
            };
            
            // Dibujar sección de la torta
            var rect = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
            canvas.DrawArc(rect, currentAngle, sweepAngle, true, sectionPaint);
            
            // Dibujar leyenda
            var legendY = 50 + i * 22;
            var legendRect = new SKRect(centerX + radius + 20, legendY - 6, centerX + radius + 32, legendY + 6);
            canvas.DrawRect(legendRect, sectionPaint);
            
            var surgeonName = surgeon.SurgeonName.Length > 12 ? surgeon.SurgeonName.Substring(0, 12) + "..." : surgeon.SurgeonName;
            var legendText = $"{surgeonName}: {surgeon.TotalSurgeries} ({percentage:F1}%)";
            canvas.DrawText(legendText, centerX + radius + 40, legendY + 3, SKTextAlign.Left, textFont, textPaint);
            
            currentAngle += sweepAngle;
        }
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    
    public byte[] GenerateTimelineChart(Dictionary<DateTime, int> timelineData, int width = 700, int height = 300)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        
        canvas.Clear(SKColors.White);
        
        if (!timelineData.Any()) return GenerateNoDataChart(width, height);
        
        var sortedData = timelineData.OrderBy(x => x.Key).ToList();
        var maxValue = Math.Max(1, sortedData.Max(x => x.Value)); // Evitar división por 0
        
        // Configuración del gráfico más amplio
        var chartArea = new SKRect(60, 50, width - 40, height - 80);
        var pointWidth = sortedData.Count > 1 ? (chartArea.Width - 40) / (sortedData.Count - 1) : chartArea.Width / 2;
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 16);
        var titleBounds = new SKRect();
        titleFont.MeasureText("Tendencia Temporal - Cirugías por Día", out titleBounds);
        canvas.DrawText("Tendencia Temporal - Cirugías por Día", (width - titleBounds.Width) / 2, 30, SKTextAlign.Left, titleFont, titlePaint);
        
        // Dibujar grilla de fondo
        using var gridPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            StrokeWidth = 1,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        
        // Líneas horizontales de la grilla
        for (int i = 0; i <= 5; i++)
        {
            var y = chartArea.Bottom - (i / 5f) * (chartArea.Height - 20);
            canvas.DrawLine(chartArea.Left, y, chartArea.Right, y, gridPaint);
        }
        
        // Dibujar línea y puntos
        using var linePaint = new SKPaint
        {
            Color = SKColor.Parse("#2196F3"),
            StrokeWidth = 3,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        
        using var pointPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        
        using var pointBorderPaint = new SKPaint
        {
            Color = SKColor.Parse("#2196F3"),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        
        using var textFont = new SKFont(SKTypeface.Default, 10);
        
        using var datePaint = new SKPaint
        {
            Color = SKColors.DarkGray,
            IsAntialias = true
        };
        
        using var dateFont = new SKFont(SKTypeface.Default, 9);
        
        var path = new SKPath();
        bool first = true;
        
        for (int i = 0; i < sortedData.Count; i++)
        {
            var point = sortedData[i];
            var x = chartArea.Left + 20 + i * pointWidth;
            var y = chartArea.Bottom - 10 - (point.Value / (float)maxValue) * (chartArea.Height - 30);
            
            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
            
            // Dibujar punto con borde
            canvas.DrawCircle(x, y, 5, pointPaint);
            canvas.DrawCircle(x, y, 5, pointBorderPaint);
            
            // Dibujar valor encima del punto
            if (point.Value > 0)
            {
                canvas.DrawText(point.Value.ToString(), x - 5, y - 12, SKTextAlign.Left, textFont, textPaint);
            }
            
            // Dibujar fecha debajo - mostrar más fechas para reportes mensuales
            var showEvery = Math.Max(1, sortedData.Count / 15); // Mostrar hasta 15 fechas
            if (i % showEvery == 0 || i == sortedData.Count - 1) // Siempre mostrar primera y última
            {
                canvas.Save();
                canvas.Translate(x, chartArea.Bottom + 15);
                canvas.RotateDegrees(-45); // Rotar texto para mejor legibilidad
                canvas.DrawText(point.Key.ToString("dd/MM"), 0, 0, SKTextAlign.Left, dateFont, datePaint);
                canvas.Restore();
            }
        }
        
        canvas.DrawPath(path, linePaint);
        path.Dispose();
        
        // Dibujar etiquetas del eje Y
        using var yAxisPaint = new SKPaint
        {
            Color = SKColors.Gray,
            IsAntialias = true
        };
        
        using var yAxisFont = new SKFont(SKTypeface.Default, 9);
        
        for (int i = 0; i <= 5; i++)
        {
            var value = (maxValue / 5f) * i;
            var y = chartArea.Bottom - (i / 5f) * (chartArea.Height - 20);
            canvas.DrawText(((int)value).ToString(), 10, y + 3, SKTextAlign.Left, yAxisFont, yAxisPaint);
        }
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    
    private byte[] GenerateNoDataChart(int width, int height)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        
        canvas.Clear(SKColors.White);
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Gray,
            IsAntialias = true
        };
        
        using var textFont = new SKFont(SKTypeface.Default, 14);
        
        canvas.DrawText("Sin datos disponibles", width / 2, height / 2, SKTextAlign.Center, textFont, textPaint);
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}