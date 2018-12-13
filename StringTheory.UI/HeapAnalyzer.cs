﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace StringTheory.UI
{
    public static class HeapAnalyzer
    {
        public static StringSummary GetStringSummary(string dumpFilePath)
        {
            using (DataTarget dataTarget = DataTarget.LoadCrashDump(dumpFilePath))
            {
                ClrInfo runtimeInfo = dataTarget.ClrVersions.First();

                ClrRuntime runtime = runtimeInfo.CreateRuntime();

                var heap = runtime.Heap;

                var tallyByString = new Dictionary<string, ObjectTally>();

                ulong stringCount = 0;
                ulong stringByteCount = 0;
                ulong totalManagedObjectCount = 0;
                ulong totalManagedObjectByteCount = 0;
                long charCount = 0;

                for (int i = 0; i < heap.Segments.Count; ++i)
                {
                    ClrSegment seg = heap.Segments[i];

                    var segType = seg.IsEphemeral
                        ? GCSegmentType.Ephemeral
                        : seg.IsLarge
                            ? GCSegmentType.LargeObject
                            : GCSegmentType.Regular;

                    for (ulong obj = seg.GetFirstObject(out ClrType type); obj != 0; obj = seg.NextObject(obj, out type))
                    {
                        if (type == null)
                        {
                            continue;
                        }

                        int generation = seg.GetGeneration(obj);

                        var size = type.GetSize(obj);

                        totalManagedObjectCount++;
                        totalManagedObjectByteCount += size;

                        if (type.IsString)
                        {
                            var value = (string) type.GetValue(obj);

                            charCount += value.Length;
                            stringCount++;

                            if (!tallyByString.TryGetValue(value, out var tally))
                            {
                                tally = new ObjectTally(size);
                                tallyByString[value] = tally;
                            }

                            stringByteCount += tally.InstanceSize;
                            tally.Add(obj, segType, generation);
                        }
                    }
                }

                var uniqueStringCount = tallyByString.Count;
                var stringCharCount = tallyByString.Sum(s => s.Key.Length * (long)s.Value.Count);
                var uniqueStringCharCount = tallyByString.Keys.Sum(s => s.Length);
                var wastedBytes = tallyByString.Values.Sum(t => (long)t.WastedBytes);
                var stringOverhead = ((double)stringByteCount - (charCount * 2)) / stringCount;

                return new StringSummary(
                    tallyByString.OrderByDescending(p => p.Value.WastedBytes)
                        .Select(p => new StringItem(
                            p.Key, 
                            (uint) p.Value.Count, 
                            (uint) p.Key.Length, 
                            p.Value.InstanceSize, 
                            p.Value.Addresses,
                            p.Value.CountBySegmentType,
                            p.Value.CountByGeneration)).ToList(),
                    totalManagedObjectByteCount,
                    stringByteCount,
                    (ulong) stringCharCount,
                    (ulong) uniqueStringCharCount,
                    stringCount,
                    (ulong) uniqueStringCount,
                    totalManagedObjectCount,
                    (ulong) wastedBytes,
                    (uint) Math.Round(stringOverhead));
            }
        }

        private sealed class ObjectTally
        {
            public ulong[] CountBySegmentType { get; } = new ulong[3];
            public ulong[] CountByGeneration { get; } = new ulong[4]; // offset by one so that -1 becomes 0
            public ulong WastedBytes => (Count - 1) * InstanceSize;
            public ulong Count => (ulong) Addresses.Count;
            public ulong InstanceSize { get; }
            public List<ulong> Addresses { get; } = new List<ulong>(capacity: 2);

            public ObjectTally(ulong size)
            {
                InstanceSize = size;
            }

            public void Add(ulong address, GCSegmentType segmentType, int generation)
            {
                Addresses.Add(address);
                CountBySegmentType[(int) segmentType]++;
                CountByGeneration[generation + 1]++;
            }
        }
    }
}