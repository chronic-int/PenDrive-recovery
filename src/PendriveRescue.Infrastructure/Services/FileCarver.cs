using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.FileSignatures;

namespace PendriveRescue.Infrastructure.Services;

public class FileCarver
{
    public List<RecoverableFile> Carve(byte[] buffer, long baseOffset)
    {
        var foundFiles = new List<RecoverableFile>();
        for (int i = 0; i < buffer.Length; i++)
        {
            foreach (var sig in SignatureDatabase.Signatures)
            {
                if (MatchSignature(buffer, i, sig.Header))
                {
                    foundFiles.Add(new RecoverableFile
                    {
                        FileName = $"Found_{sig.Extension.TrimStart('.')}_{baseOffset + i:X}",
                        Extension = sig.Extension,
                        StartOffset = baseOffset + i,
                        Confidence = RecoveryConfidence.Medium, // Carver finding header is medium-high confidence
                        State = RecoveryState.Pending,
                        SizeBytes = 0 // Carver doesn't know size yet; will need trailer or fixed length
                    });

                    // Skip the length of signature to avoid redundant matches if needed
                    i += sig.Header.Length - 1;
                    break;
                }
            }
        }

        return foundFiles;
    }

    private bool MatchSignature(byte[] buffer, int offset, byte[] signature)
    {
        if (offset + signature.Length > buffer.Length) return false;

        for (int j = 0; j < signature.Length; j++)
        {
            if (buffer[offset + j] != signature[j]) return false;
        }

        return true;
    }
}
