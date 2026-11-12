using System.Diagnostics;

namespace VelaRay.Core.Runtime;

/// <summary>
/// Platform-specific ownership of processes launched by VelaRay. An implementation
/// must ensure they cannot survive abnormal termination of the UI process.
/// </summary>
public interface IChildProcessLifetime : IDisposable
{
    /// <summary>Take ownership immediately after Process.Start; throw if ownership fails.</summary>
    void Attach(Process process);
}
