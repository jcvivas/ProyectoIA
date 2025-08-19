namespace music.Options
{
    public class InferenceOptions
    {
        public const string SectionName = "Inference";
        public string PythonExe { get; set; } = "";
        public string ScriptPath { get; set; } = "";
        public string ModelPath { get; set; } = "";
        public string LabelsPath { get; set; } = "";
        public string EmbeddingsIndexPath { get; set; } = ""; // si luego agregas recomendaciones por embeddings
        public string SongLibraryDir { get; set; } = "";
        public string TempDir { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 60;
    }
}
