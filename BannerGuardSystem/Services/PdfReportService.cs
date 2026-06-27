using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public class PdfReportService
{
    public void GenerateReport(BannerDetectionEventDto alert, string outputPath)
    {
        // Ensure the directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20, Unit.Point);
                page.PageColor(Colors.White);
                
                // Set default style with Korean font (Malgun Gothic)
                page.DefaultTextStyle(x => x.FontFamily("Malgun Gothic").FontSize(11).FontColor(Colors.Grey.Darken4));

                // Header section
                page.Header().Element(ComposeHeader);

                // Content section
                page.Content().Element(c => ComposeContent(c, alert));

                // Footer section
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(outputPath);
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("불법 옥외광고물 단속 및 행정 조치 요청서")
                .FontFamily("Malgun Gothic")
                .FontSize(20)
                .Bold()
                .FontColor(Colors.Grey.Darken4);
            
            column.Item().PaddingTop(10).LineHorizontal(1.5f).LineColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeContent(IContainer container, BannerDetectionEventDto alert)
    {
        container.PaddingTop(15).Column(column =>
        {
            // 1. Metadata Table Section
            column.Item().Text("1. 단속 대상 정보").Bold().FontSize(13).FontColor(Colors.Grey.Darken3);
            
            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(2);
                });

                // Header cell style
                static IContainer HeaderStyle(IContainer cell) => cell
                    .Border(0.5f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Background(Colors.Grey.Lighten4)
                    .Padding(5)
                    .AlignMiddle();

                // Value cell style
                static IContainer ValueStyle(IContainer cell) => cell
                    .Border(0.5f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Background(Colors.White)
                    .Padding(5)
                    .AlignMiddle();

                // Row 1
                HeaderStyle(table.Cell()).Text("단속 일시").Bold();
                ValueStyle(table.Cell()).Text(alert.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                
                HeaderStyle(table.Cell()).Text("CCTV ID").Bold();
                ValueStyle(table.Cell()).Text(alert.CctvId);

                // Row 2
                HeaderStyle(table.Cell()).Text("단속 위치").Bold();
                ValueStyle(table.Cell()).Text(alert.LocationAddress);

                HeaderStyle(table.Cell()).Text("상습 여부").Bold();
                string repeatText = alert.IsRepeatOffender ? $"예 (누적 {alert.RepeatCount}회)" : "아니오";
                ValueStyle(table.Cell()).Text(repeatText);
            });

            // 2. Evidence Image Section
            column.Item().PaddingTop(15).Text("2. 단속 증거 사진").Bold().FontSize(13).FontColor(Colors.Grey.Darken3);
            
            byte[]? imgBytes = null;
            if (!string.IsNullOrEmpty(alert.CroppedImageBase64))
            {
                try
                {
                    imgBytes = Convert.FromBase64String(alert.CroppedImageBase64);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PdfReportService] Base64 decoding failed: {ex.Message}");
                }
            }

            if (imgBytes == null && !string.IsNullOrEmpty(alert.CropImageUri))
            {
                try
                {
                    string fullPath = alert.CropImageUri;
                    if (!Path.IsPathRooted(fullPath))
                    {
                        // Check multiple potential relative paths
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string tempPath = Path.Combine(baseDir, alert.CropImageUri);
                        if (File.Exists(tempPath))
                        {
                            fullPath = tempPath;
                        }
                        else
                        {
                            // Fallback to project root directory
                            fullPath = Path.Combine(@"d:\138.종합 민원 이미지 AI데이터\BannerGuardSystem", alert.CropImageUri);
                        }
                    }

                    if (File.Exists(fullPath))
                    {
                        imgBytes = File.ReadAllBytes(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PdfReportService] Reading image file failed: {ex.Message}");
                }
            }

            if (imgBytes != null)
            {
                column.Item().PaddingTop(5).AlignCenter().Width(400).Height(240).Image(imgBytes).FitArea();
            }
            else
            {
                column.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten4).Height(150).AlignCenter().AlignMiddle().Text("증거 사진 없음 (이미지 데이터 미수신)").FontColor(Colors.Grey.Darken1);
            }

            // 3. Legal/Administrative grounds Section
            column.Item().PaddingTop(15).Text("3. 단속 근거 및 AI 분석 결과").Bold().FontSize(13).FontColor(Colors.Grey.Darken3);
            
            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(3);
                });

                // Style helpers
                static IContainer HeaderStyle(IContainer cell) => cell
                    .Border(0.5f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Background(Colors.Grey.Lighten4)
                    .Padding(5)
                    .AlignMiddle();

                static IContainer ValueStyle(IContainer cell) => cell
                    .Border(0.5f)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Background(Colors.White)
                    .Padding(5)
                    .AlignMiddle();

                // Row 1
                HeaderStyle(table.Cell()).Text("EasyOCR 판독").Bold();
                string ocrDisplayText = string.IsNullOrEmpty(alert.OcrText) ? "(식별된 문자 없음)" : alert.OcrText;
                if (alert.OcrConfidence > 0)
                {
                    ocrDisplayText += $" (신뢰도: {alert.OcrConfidence * 100:F1}%)";
                }
                ValueStyle(table.Cell()).Text(ocrDisplayText);

                // Row 2
                HeaderStyle(table.Cell()).Text("VLM 심층 판단").Bold();
                string vlmText = alert.IsVlmTriggered 
                    ? alert.VlmAnalysis 
                    : "미트리거 (OCR 신뢰 기준 부합에 의한 자동 판정)";
                ValueStyle(table.Cell()).Text(vlmText);
            });

            // Explanatory Paragraph
            column.Item().PaddingTop(10).Text(text =>
            {
                text.ParagraphSpacing(5);
                text.Line("본 광고물은 관련 법령(옥외광고물 등의 관리와 옥외광고산업 진흥에 관한 법률 제3조 및 제10조)에 의거하여 허가 또는 신고를 거치지 않고 무단으로 표시·설치된 불법 광고물로 판정되었습니다.").FontSize(9.5f);
                text.Line("CCTV 실시간 탐지 시스템(YOLOv8/v11 객체 탐지 및 EasyOCR 텍스트 인식)과 Qwen2.5-VL 기반 다중 에이전트 합의 판정 알고리즘을 통해 자동 적발되었으며, 상기 증거 사진에 기반하여 위법 사실이 명백하므로 행정 조치(강제 철거 등)를 요청합니다.").FontSize(9.5f);
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            
            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().AlignLeft().AlignMiddle().Column(col =>
                {
                    col.Item().Text("인천광역시 관할 구역 도시재생과 광고물관리팀")
                        .FontFamily("Malgun Gothic")
                        .FontSize(10)
                        .Bold()
                        .FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(2).Text("시스템 발급 문서 (직인 생략)")
                        .FontFamily("Malgun Gothic")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken1);
                });
                
                row.ConstantItem(120).AlignRight().AlignMiddle().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5).AlignCenter().Text("확인 (서명란)")
                    .FontFamily("Malgun Gothic")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }
}
