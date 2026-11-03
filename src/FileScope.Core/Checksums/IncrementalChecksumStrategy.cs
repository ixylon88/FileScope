using System.Security.Cryptography;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Checksums;

public sealed class IncrementalChecksumStrategy : IChecksumStrategy
{
    private readonly IncrementalHash _hash;
    private bool _completed;
    public ChecksumAlgorithm Algorithm { get; }

    public IncrementalChecksumStrategy(ChecksumAlgorithm algorithm, HashAlgorithmName name)
    {
        Algorithm = algorithm;
        _hash = IncrementalHash.CreateHash(name);
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_completed) throw new InvalidOperationException("The checksum has already been finalized.");
        _hash.AppendData(data);
    }

    public string Complete()
    {
        if (_completed) throw new InvalidOperationException("The checksum has already been finalized.");
        _completed = true;
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public void Dispose() => _hash.Dispose();
}

public sealed class ChecksumStrategyFactory : IChecksumStrategyFactory
{
    public IChecksumStrategy Create(ChecksumAlgorithm algorithm) => new IncrementalChecksumStrategy(
        algorithm, algorithm switch
        {
            ChecksumAlgorithm.MD5 => HashAlgorithmName.MD5,
            ChecksumAlgorithm.SHA1 => HashAlgorithmName.SHA1,
            ChecksumAlgorithm.SHA256 => HashAlgorithmName.SHA256,
            ChecksumAlgorithm.SHA384 => HashAlgorithmName.SHA384,
            ChecksumAlgorithm.SHA512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        });
}
