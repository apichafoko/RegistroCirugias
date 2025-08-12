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
        
        // Preparar datos para gráfico de barras
        var topSurgeries = surgeryTypeData.OrderByDescending(x => x.Value).Take(8).ToList();
        var maxValue = topSurgeries.Max(x => x.Value);
        
        // Configuración del gráfico
        var chartArea = new SKRect(80, 40, width - 40, height - 80);
        var barWidth = (chartArea.Width - 40) / topSurgeries.Count;
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        var titleBounds = new SKRect();
        titlePaint.MeasureText("Cirugías por Tipo", ref titleBounds);
        canvas.DrawText("Cirugías por Tipo", (width - titleBounds.Width) / 2, 25, titlePaint);
        
        // Dibujar barras
        using var barPaint = new SKPaint
        {
            Color = SKColor.Parse("#4A90E2"),
            IsAntialias = true
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 10,
            IsAntialias = true
        };
        
        for (int i = 0; i < topSurgeries.Count; i++)
        {
            var surgery = topSurgeries[i];
            var barHeight = (surgery.Value / (float)maxValue) * (chartArea.Height - 40);
            var x = chartArea.Left + i * barWidth + 10;
            var y = chartArea.Bottom - barHeight;
            
            // Dibujar barra
            canvas.DrawRect(x, y, barWidth - 20, barHeight, barPaint);
            
            // Dibujar valor encima de la barra
            canvas.DrawText(surgery.Value.ToString(), x + (barWidth - 20) / 2 - 10, y - 5, textPaint);
            
            // Dibujar etiqueta (rotada si es necesaria)
            var label = surgery.Key.Length > 8 ? surgery.Key.Substring(0, 8) + "..." : surgery.Key;
            canvas.DrawText(label, x, chartArea.Bottom + 15, textPaint);
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
        
        var maxValue = topSurgeons.Max(x => x.TotalSurgeries);
        
        // Configuración del gráfico
        var chartArea = new SKRect(100, 40, width - 40, height - 80);
        var barHeight = (chartArea.Height - 40) / topSurgeons.Count;
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        var titleBounds = new SKRect();
        titlePaint.MeasureText("Top Cirujanos por Volumen", ref titleBounds);
        canvas.DrawText("Top Cirujanos por Volumen", (width - titleBounds.Width) / 2, 25, titlePaint);
        
        // Dibujar barras horizontales
        using var barPaint = new SKPaint
        {
            Color = SKColor.Parse("#28A745"),
            IsAntialias = true
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 10,
            IsAntialias = true
        };
        
        for (int i = 0; i < topSurgeons.Count; i++)
        {
            var surgeon = topSurgeons[i];
            var barWidth = (surgeon.TotalSurgeries / (float)maxValue) * (chartArea.Width - 120);
            var y = chartArea.Top + i * barHeight + 10;
            
            // Dibujar barra horizontal
            canvas.DrawRect(chartArea.Left, y, barWidth, barHeight - 20, barPaint);
            
            // Dibujar nombre del cirujano
            var name = surgeon.SurgeonName.Length > 15 ? surgeon.SurgeonName.Substring(0, 15) + "..." : surgeon.SurgeonName;
            canvas.DrawText(name, 10, y + (barHeight - 20) / 2 + 5, textPaint);
            
            // Dibujar valor al final de la barra
            canvas.DrawText(surgeon.TotalSurgeries.ToString(), chartArea.Left + barWidth + 5, y + (barHeight - 20) / 2 + 5, textPaint);
        }
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    
    public byte[] GenerateTimelineChart(Dictionary<DateTime, int> timelineData, int width = 500, int height = 250)
    {
        var info = new SKImageInfo(width, height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        
        canvas.Clear(SKColors.White);
        
        if (!timelineData.Any()) return GenerateNoDataChart(width, height);
        
        var sortedData = timelineData.OrderBy(x => x.Key).ToList();
        var maxValue = sortedData.Max(x => x.Value);
        
        // Configuración del gráfico
        var chartArea = new SKRect(60, 40, width - 40, height - 60);
        var pointWidth = (chartArea.Width - 20) / Math.Max(1, sortedData.Count - 1);
        
        // Dibujar título
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        var titleBounds = new SKRect();
        titlePaint.MeasureText("Tendencia Temporal", ref titleBounds);
        canvas.DrawText("Tendencia Temporal", (width - titleBounds.Width) / 2, 25, titlePaint);
        
        // Dibujar línea y puntos
        using var linePaint = new SKPaint
        {
            Color = SKColor.Parse("#FF6B6B"),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        
        using var pointPaint = new SKPaint
        {
            Color = SKColor.Parse("#FF6B6B"),
            IsAntialias = true
        };
        
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 8,
            IsAntialias = true
        };
        
        var path = new SKPath();
        bool first = true;
        
        for (int i = 0; i < sortedData.Count; i++)
        {
            var point = sortedData[i];
            var x = chartArea.Left + i * pointWidth;
            var y = chartArea.Bottom - (point.Value / (float)maxValue) * (chartArea.Height - 20);
            
            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
            
            // Dibujar punto
            canvas.DrawCircle(x, y, 3, pointPaint);
            
            // Dibujar valor
            canvas.DrawText(point.Value.ToString(), x - 5, y - 8, textPaint);
            
            // Dibujar fecha (solo cada 2-3 puntos para evitar solapamiento)
            if (i % Math.Max(1, sortedData.Count / 6) == 0)
            {
                canvas.DrawText(point.Key.ToString("dd/MM"), x - 10, chartArea.Bottom + 15, textPaint);
            }
        }
        
        canvas.DrawPath(path, linePaint);
        path.Dispose();
        
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
            TextSize = 14,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };
        
        canvas.DrawText("Sin datos disponibles", width / 2, height / 2, textPaint);
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}