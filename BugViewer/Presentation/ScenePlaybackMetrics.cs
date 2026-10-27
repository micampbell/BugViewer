namespace BugViewer;

/// <summary>One completed presentation update. Browser timings describe submission, not GPU execution.</summary>
public sealed record ScenePlaybackMetrics
{
    /// <summary>Zero-based step whose frame was submitted.</summary>
    public int Step { get; init; }
    /// <summary>Time spent resolving and preparing source geometry.</summary>
    public double PreparationMs { get; init; }
    /// <summary>Time spent updating bounds, selection, and display geometry.</summary>
    public double SceneUpdateMs { get; init; }
    /// <summary>Time spent packing missing geometry into binary data.</summary>
    public double PackingMs { get; init; }
    /// <summary>Interop, upload, and first-frame scheduling time.</summary>
    public double InteropMs { get; init; }
    /// <summary>Active frame update through submission; excludes waiting behind another update or prefetch.</summary>
    public double TotalMs { get; init; }
    /// <summary>Geometry bytes transferred for this step; cached steps transfer none.</summary>
    public long UploadedBytes { get; init; }
    /// <summary>Number of desired resources already resident on the GPU.</summary>
    public int CacheHits { get; init; }
    /// <summary>Browser time spent creating missing GPU resources.</summary>
    public double BrowserUploadMs { get; init; }
    /// <summary>Browser timestamp at submission, useful for counting distinct steps per second.</summary>
    public double SubmittedAtMs { get; init; }
    /// <summary>CPU time used to encode and submit the first frame.</summary>
    public double SubmissionMs { get; init; }
    /// <summary>Whether the document was visible at submission.</summary>
    public bool Visible { get; init; }
    /// <summary>Number of missed playback deadlines since playback started. Steps are not skipped.</summary>
    public int MissedDeadlines { get; init; }
}
