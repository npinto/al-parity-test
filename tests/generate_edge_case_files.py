#!/usr/bin/env python3
"""
Generate edge case test files for target.dll coverage testing.

This script creates audio files that exercise various code paths:
- Different bit depths (8, 16, 24, 32-bit int, 32-bit float)
- Different sample rates (44100, 48000, 96000)
- Different channel counts (mono, stereo)
- Edge case values (silence, clipping, DC offset)
"""

import struct
import os
from pathlib import Path

TEST_FILES_DIR = Path(__file__).parent / "test_files"


def write_wav(filename: str, samples: list, sample_rate: int = 48000,
              bits_per_sample: int = 16, num_channels: int = 1):
    """Write a WAV file with given parameters."""
    filepath = TEST_FILES_DIR / filename

    # Calculate parameters
    num_samples = len(samples) // num_channels
    bytes_per_sample = bits_per_sample // 8
    byte_rate = sample_rate * num_channels * bytes_per_sample
    block_align = num_channels * bytes_per_sample
    data_size = len(samples) * bytes_per_sample

    with open(filepath, "wb") as f:
        # RIFF header
        f.write(b"RIFF")
        f.write(struct.pack("<I", 36 + data_size))  # File size - 8
        f.write(b"WAVE")

        # fmt chunk
        f.write(b"fmt ")
        f.write(struct.pack("<I", 16))  # Chunk size
        audio_format = 3 if bits_per_sample == 32 else 1  # 3 = float, 1 = PCM
        f.write(struct.pack("<H", audio_format))  # Audio format
        f.write(struct.pack("<H", num_channels))  # Num channels
        f.write(struct.pack("<I", sample_rate))  # Sample rate
        f.write(struct.pack("<I", byte_rate))  # Byte rate
        f.write(struct.pack("<H", block_align))  # Block align
        f.write(struct.pack("<H", bits_per_sample))  # Bits per sample

        # data chunk
        f.write(b"data")
        f.write(struct.pack("<I", data_size))

        # Write samples
        for sample in samples:
            # Clamp sample to [-1, 1]
            sample = max(-1.0, min(1.0, sample))

            if bits_per_sample == 8:
                # 8-bit is unsigned (0-255, center at 128)
                val = int(sample * 127 + 128)
                f.write(struct.pack("B", max(0, min(255, val))))
            elif bits_per_sample == 16:
                val = int(sample * 32767)
                f.write(struct.pack("<h", max(-32768, min(32767, val))))
            elif bits_per_sample == 24:
                val = int(sample * 8388607)
                val = max(-8388608, min(8388607, val))
                # Pack as 3 bytes little-endian
                if val < 0:
                    val = val + 16777216  # Convert to unsigned
                f.write(struct.pack("<I", val)[:3])
            elif bits_per_sample == 32:
                # 32-bit float
                f.write(struct.pack("<f", float(sample)))

    print(f"[OK] Generated: {filename} ({sample_rate}Hz, {bits_per_sample}-bit, {num_channels}ch)")
    return filepath


def write_etx(filename: str, samples: list, sample_rate: int = 48000):
    """Write an AudioMeasure Text Export (ETX) file."""
    filepath = TEST_FILES_DIR / filename

    dt = 1.0 / sample_rate

    with open(filepath, "w") as f:
        f.write("# AudioMeasure Text Export\n")
        f.write("# Time (seconds)    Value\n")
        for i, sample in enumerate(samples):
            f.write(f"{i * dt:.10f}\t{sample:.8f}\n")

    print(f"[OK] Generated: {filename} ({len(samples)} samples)")
    return filepath


def generate_sine_wave(num_samples: int, freq: float, sample_rate: int = 48000) -> list:
    """Generate a sine wave."""
    import math
    return [math.sin(2 * math.pi * freq * i / sample_rate) for i in range(num_samples)]


def generate_silence(num_samples: int) -> list:
    """Generate silence."""
    return [0.0] * num_samples


def generate_dc_offset(num_samples: int, offset: float = 0.5) -> list:
    """Generate samples with a DC offset."""
    return [offset] * num_samples


def generate_clipping(num_samples: int) -> list:
    """Generate samples that clip at max/min values."""
    result = []
    for i in range(num_samples):
        if i % 4 == 0:
            result.append(1.0)  # Max positive
        elif i % 4 == 1:
            result.append(-1.0)  # Max negative
        elif i % 4 == 2:
            result.append(0.999)  # Near max
        else:
            result.append(-0.999)  # Near min
    return result


def main():
    print("=" * 60)
    print("Generating Edge Case Test Files for target.dll Coverage")
    print("=" * 60)

    os.makedirs(TEST_FILES_DIR, exist_ok=True)

    # 1. Bit depth variations (16-bit, 8-bit, 24-bit, 32-bit float)
    sine_1k = generate_sine_wave(4800, 1000.0)  # 0.1s of 1kHz sine

    write_wav("edge_8bit_mono.wav", sine_1k, bits_per_sample=8)
    write_wav("edge_16bit_mono.wav", sine_1k, bits_per_sample=16)
    write_wav("edge_24bit_mono.wav", sine_1k, bits_per_sample=24)
    write_wav("edge_32bit_float.wav", sine_1k, bits_per_sample=32)

    # 2. Sample rate variations
    write_wav("edge_44100Hz.wav", generate_sine_wave(4410, 1000.0, 44100), sample_rate=44100)
    write_wav("edge_48000Hz.wav", sine_1k, sample_rate=48000)
    write_wav("edge_96000Hz.wav", generate_sine_wave(9600, 1000.0, 96000), sample_rate=96000)

    # 3. Channel variations
    stereo_samples = []
    for s in sine_1k:
        stereo_samples.append(s)  # Left
        stereo_samples.append(s * 0.5)  # Right (half amplitude)
    write_wav("edge_stereo.wav", stereo_samples, num_channels=2)

    # 4. Special values
    write_wav("edge_silence.wav", generate_silence(4800))
    write_wav("edge_dc_offset.wav", generate_dc_offset(4800, 0.5))
    write_wav("edge_clipping.wav", generate_clipping(4800))

    # 5. Very short file
    write_wav("edge_short.wav", [0.5, -0.5, 0.25, -0.25])

    # 6. ETX format (text)
    write_etx("edge_test.etx", sine_1k[:1000])  # 1000 samples

    # 7. 32-bit integer (not float)
    write_wav_32bit_int("edge_32bit_int.wav", sine_1k)

    # 8. 64-bit float (double)
    write_wav_64bit_float("edge_64bit_float.wav", sine_1k)

    # 9. Malformed/edge case files
    generate_malformed_files()

    # Summary
    print("\n" + "=" * 60)
    print("Edge Case Files Generated:")
    print("=" * 60)
    for f in sorted(TEST_FILES_DIR.glob("edge_*")):
        print(f"  {f.name}: {f.stat().st_size} bytes")
    for f in sorted(TEST_FILES_DIR.glob("malformed_*")):
        print(f"  {f.name}: {f.stat().st_size} bytes")


def write_wav_32bit_int(filename: str, samples: list, sample_rate: int = 48000):
    """Write a 32-bit integer WAV file."""
    filepath = TEST_FILES_DIR / filename

    num_samples = len(samples)
    bytes_per_sample = 4
    num_channels = 1
    byte_rate = sample_rate * num_channels * bytes_per_sample
    block_align = num_channels * bytes_per_sample
    data_size = num_samples * bytes_per_sample

    with open(filepath, "wb") as f:
        # RIFF header
        f.write(b"RIFF")
        f.write(struct.pack("<I", 36 + data_size))
        f.write(b"WAVE")

        # fmt chunk - audio format 1 = PCM integer
        f.write(b"fmt ")
        f.write(struct.pack("<I", 16))
        f.write(struct.pack("<H", 1))  # PCM integer, not float
        f.write(struct.pack("<H", num_channels))
        f.write(struct.pack("<I", sample_rate))
        f.write(struct.pack("<I", byte_rate))
        f.write(struct.pack("<H", block_align))
        f.write(struct.pack("<H", 32))  # 32-bit

        # data chunk
        f.write(b"data")
        f.write(struct.pack("<I", data_size))

        for sample in samples:
            sample = max(-1.0, min(1.0, sample))
            val = int(sample * 2147483647)
            f.write(struct.pack("<i", val))

    print(f"[OK] Generated: {filename} (32-bit integer)")
    return filepath


def write_wav_64bit_float(filename: str, samples: list, sample_rate: int = 48000):
    """Write a 64-bit float (double) WAV file."""
    filepath = TEST_FILES_DIR / filename

    num_samples = len(samples)
    bytes_per_sample = 8
    num_channels = 1
    byte_rate = sample_rate * num_channels * bytes_per_sample
    block_align = num_channels * bytes_per_sample
    data_size = num_samples * bytes_per_sample

    with open(filepath, "wb") as f:
        # RIFF header
        f.write(b"RIFF")
        f.write(struct.pack("<I", 36 + data_size))
        f.write(b"WAVE")

        # fmt chunk - audio format 3 = float
        f.write(b"fmt ")
        f.write(struct.pack("<I", 16))
        f.write(struct.pack("<H", 3))  # IEEE float
        f.write(struct.pack("<H", num_channels))
        f.write(struct.pack("<I", sample_rate))
        f.write(struct.pack("<I", byte_rate))
        f.write(struct.pack("<H", block_align))
        f.write(struct.pack("<H", 64))  # 64-bit

        # data chunk
        f.write(b"data")
        f.write(struct.pack("<I", data_size))

        for sample in samples:
            f.write(struct.pack("<d", float(sample)))

    print(f"[OK] Generated: {filename} (64-bit float)")
    return filepath


def generate_malformed_files():
    """Generate malformed files to test error handling paths."""
    print("\n--- Generating Malformed Test Files ---")

    # 1. Empty file
    (TEST_FILES_DIR / "malformed_empty.wav").write_bytes(b"")
    print("[OK] Generated: malformed_empty.wav (0 bytes)")

    # 2. Truncated WAV header (only RIFF)
    (TEST_FILES_DIR / "malformed_truncated_header.wav").write_bytes(b"RIFF\x00\x00\x00\x00")
    print("[OK] Generated: malformed_truncated_header.wav (8 bytes)")

    # 3. Invalid RIFF magic
    (TEST_FILES_DIR / "malformed_bad_magic.wav").write_bytes(b"XXXX" + b"\x00" * 40)
    print("[OK] Generated: malformed_bad_magic.wav (44 bytes)")

    # 4. WAV with corrupt fmt chunk (wrong size)
    data = b"RIFF" + struct.pack("<I", 100) + b"WAVE"
    data += b"fmt " + struct.pack("<I", 999)  # Absurd fmt size
    (TEST_FILES_DIR / "malformed_bad_fmt.wav").write_bytes(data)
    print("[OK] Generated: malformed_bad_fmt.wav (corrupt fmt chunk)")

    # 5. WAV with missing data chunk
    data = b"RIFF" + struct.pack("<I", 36) + b"WAVE"
    data += b"fmt " + struct.pack("<I", 16)
    data += struct.pack("<HHIIHH", 1, 1, 48000, 96000, 2, 16)  # Valid fmt
    (TEST_FILES_DIR / "malformed_no_data.wav").write_bytes(data)
    print("[OK] Generated: malformed_no_data.wav (missing data chunk)")

    # 6. ETM with wrong magic
    data = b"NOT AmFmt  " + b"\x00" * 244
    (TEST_FILES_DIR / "malformed_bad_etm.etm").write_bytes(data)
    print("[OK] Generated: malformed_bad_etm.etm (wrong magic)")

    # 7. EMD with embedded but no valid measurement header
    data = b"Measurement10MS" + b"\x00" * 500
    (TEST_FILES_DIR / "malformed_bad_emd.emd").write_bytes(data)
    print("[OK] Generated: malformed_bad_emd.emd (EMD without embedded ETM)")

    # 8. ETX with no valid data lines
    etx_data = "# Comment only\n# Another comment\n\n"
    (TEST_FILES_DIR / "malformed_empty_etx.etx").write_text(etx_data)
    print("[OK] Generated: malformed_empty_etx.etx (no data lines)")

    # 9. WAV with zero sample rate
    data = b"RIFF" + struct.pack("<I", 44) + b"WAVE"
    data += b"fmt " + struct.pack("<I", 16)
    data += struct.pack("<HHIIHH", 1, 1, 0, 0, 2, 16)  # Zero sample rate!
    data += b"data" + struct.pack("<I", 0)
    (TEST_FILES_DIR / "malformed_zero_rate.wav").write_bytes(data)
    print("[OK] Generated: malformed_zero_rate.wav (zero sample rate)")

    # 10. Multi-channel WAV (4 channels)
    samples = [0.5] * 400  # 100 samples * 4 channels
    write_wav("edge_4channel.wav", samples, num_channels=4)


if __name__ == "__main__":
    main()
