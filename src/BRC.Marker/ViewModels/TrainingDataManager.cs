using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BRC.Marker.ViewModels;

public static class TrainingDataManager
{
    // 定义 LabelMe JSON 的数据结构用于反序列化
    private class LabelMeData
    {
        [JsonPropertyName("shapes")]
        public List<Shape> Shapes { get; set; } = new();
        [JsonPropertyName("imagePath")]
        public string ImagePath { get; set; } = "";
    }

    private class Shape
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";
        [JsonPropertyName("points")]
        public List<List<double>> Points { get; set; } = new();
    }

    /// <summary>
    /// 核心功能：扫描文件夹，读取JSON，生成 train.txt 和 val.txt
    /// </summary>
    public static void PrepareTrainingData()
    {
        
    }
}