using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DynamicData.Kernel;

namespace TailBlazer.Domain.FileHandling
{
    public class SparseIndexCollection: IIndexCollection
    {
        public Encoding Encoding { get; }
        public int Count { get; }
        public int Diff { get; }
        public LinesChangedReason ChangedReason { get; }
        public long TailStartsAt { get; }
        private SparseIndex[] Indicies { get; }

        public SparseIndexCollection(IReadOnlyCollection<SparseIndex> latest,
                                    SparseIndexCollection previous,
                                    Encoding encoding)
        {

            Encoding = encoding;
            Count = latest.Select(idx => idx.LineCount).Sum();
            Indicies = latest.ToArray();
            Diff = Count - (previous?.Count ?? 0);

            //need to check whether
            if (previous == null)
            {
                ChangedReason = LinesChangedReason.Loaded;
                TailStartsAt = Count-1;
            }
            else
            {
                var mostRecent = latest.OrderByDescending(l => l.TimeStamp).First();
                ChangedReason = mostRecent.Type == IndexType.Tail
                                ? LinesChangedReason.Tailed
                                : LinesChangedReason.Paged;

                TailStartsAt = previous.Count-1;
            }
        }




        public IEnumerable<LineIndex> GetIndicies(ScrollRequest scroll)
        {
            int first = scroll.FirstIndex;
            int size = scroll.PageSize;

            if (scroll.Mode == ScrollingMode.Tail)
            {
                first = size > Count ? 0 : Count - size;
            }
            else
            {
                if (scroll.FirstIndex + size >= Count)
                    first = Count - size;
            }
            
            var relativeIndex = CalculateRelativeIndex(first);
            if (relativeIndex==null) yield break;

            var offset = relativeIndex.LinesOffset;
            foreach (var i in Enumerable.Range(first, Math.Min(size, Count)))
            {
                yield return  new LineIndex(i + 1, i, relativeIndex.Start, offset);
                offset++;
            }
        }

        public long GetLineNumberFromPosition(long position)
        {
            throw new NotImplementedException();
        }

        public LineIndex GetLineNumberPosition(int index,long endPosition)
        {
            int firstLineInContainer = 0;
            int lastLineInContainer = 0;

            foreach (var sparseIndex in Indicies)
            {
                lastLineInContainer += sparseIndex.LineCount;
                if (endPosition >= sparseIndex.Start && endPosition <= sparseIndex.End)
                {
                    //It could be that the user is scrolling into a part of the file
                    //which is still being indexed [or will never be indexed]. 
                    //In this case we need to estimate where to scroll to
                    if (sparseIndex.LineCount != 0 && sparseIndex.Indicies.Count == 0)
                    {
                        //return estimate here!
                        var lines = sparseIndex.LineCount;
                        var bytes = sparseIndex.End - sparseIndex.Start;
                        var bytesPerLine = bytes / lines;
                        long lineNumber = (endPosition / bytesPerLine);
                        throw new IndexOutOfRangeException("Cannot find matching line");
                      //  return new LineIndex(lineNumber, index,);

                        //return estimate;
                    }

                    //var linenumber is the indexof the positon [could be shit perf because expensive indexOf]
                    var aboluteIndex = sparseIndex.Indicies.IndexOf(endPosition);

                    if (aboluteIndex==-1)
                       Console.WriteLine();
                    var start = aboluteIndex == 0 ? 0 : sparseIndex.Indicies[aboluteIndex - 1];
                    return new LineIndex(aboluteIndex, index + firstLineInContainer, start, endPosition);

                }
                firstLineInContainer = firstLineInContainer + sparseIndex.LineCount;
            }
            throw new IndexOutOfRangeException("Cannot find matching line");
        }

        public IEnumerable<LineIndex> GetIndicies(ScrollRequest scroll, LineMatches matches)
        {
            //abstract clipping
            int first = scroll.FirstIndex;
            int size = scroll.PageSize;

            if (scroll.Mode == ScrollingMode.Tail)
            {
                first = size > matches.Count ? 0 : matches.Count - size;
            }
            else
            {
                if (first + size >= matches.Count)
                    first = matches.Count - size;
            }

            var allMatched = Enumerable.Range(Math.Max(first, 0), Math.Min(size, matches.Count));

            int i = 0;
            foreach (var item in allMatched)
            {
                if (item >= matches.Count) continue;
                var line = matches.Lines[item];
                
                var relativeIndex = CalculateRelativeIndex(line);
                if (relativeIndex == null) yield break;

                yield return new LineIndex(line, i + first, relativeIndex.Start, relativeIndex.LinesOffset);
                i++;
            }
        }

        private RelativeIndex CalculateRelativeIndex(int index)
        {
            int firstLineInContainer = 0;
            int lastLineInContainer = 0;

            foreach (var sparseIndex in Indicies)
            {
                lastLineInContainer += sparseIndex.LineCount;
                if (index < lastLineInContainer)
                {
                    //It could be that the user is scrolling into a part of the file
                    //which is still being indexed [or will never be indexed]. 
                    //In this case we need to estimate where to scroll to
                    if (sparseIndex.LineCount != 0 && sparseIndex.Indicies.Count == 0)
                    {
                        //return estimate here!
                        var lines = sparseIndex.LineCount;
                        var bytes = sparseIndex.End - sparseIndex.Start;
                        var bytesPerLine = bytes/lines;
                        var estimate = index*bytesPerLine;
                        return new RelativeIndex(index, estimate, 0);
                    }

                    var relativePosition = (index - firstLineInContainer);
                    var relativeIndex = relativePosition / sparseIndex.Compression;
                    var offset = relativePosition % sparseIndex.Compression;
                    var start = relativeIndex == 0 ? 0 : sparseIndex.Indicies[relativeIndex - 1];
                    return new RelativeIndex(index, start, offset);
                }
                firstLineInContainer = firstLineInContainer + sparseIndex.LineCount;
            }
            return null;
        }

        private class RelativeIndex
        {
            public int Index { get; }
            public long Start { get; }
            public int LinesOffset { get; }

            public RelativeIndex(int index, long start, int linesOffset)
            {
                Index = index;
                Start = start;
                LinesOffset = linesOffset;
            }
        }
    }
}