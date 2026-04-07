namespace BRC.Marker.Pages
{
    // 用于在页面之间传递参数
    public class SpectrogramConfig
    {
        public string InputPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string FilePattern { get; set; } = "*.tdms";
        public int WindowSize { get; set; } = 2048;
        public int HopSize { get; set; } = 512;
        public int Channels { get; set; } = 1;
        public int SampleRate { get; set; } = 12800;

        // === 新增下面这两个属性 ===
        public bool UseCombinedOkAlgorithm { get; set; } = false;
        public string OkDataFolderPath { get; set; } = "";
        // === 补上这行，解决报错 ===
        public bool IsSelfNorm { get; set; } = true;
    }
}