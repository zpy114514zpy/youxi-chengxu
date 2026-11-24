using UnityEditor;
using UnityEngine;

/// <summary>背景音乐用流式加载，别在开场一次性解出十几兆 PCM；短音效仍然整段解压（延迟最低）。</summary>
public sealed class GameAudioImport : AssetPostprocessor
{
    void OnPreprocessAudio()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/GameAudio/")) return;
        var importer = assetImporter as AudioImporter;
        if (!importer) return;

        var settings = importer.defaultSampleSettings;
        bool isBgm = assetPath.Contains("bgm_");
        settings.loadType = isBgm ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = !isBgm;      // 短音效提前解好，按下去就响；BGM 流式读，不预载
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = isBgm;
        importer.forceToMono = false;
    }
}
