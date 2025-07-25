using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// カーブ制御点の選択状態を管理するViewModel
    /// </summary>
    public class CurveSelectionViewModel : IEnumerable<int> {
        /// <summary>
        /// 選択中の制御点インデックス
        /// </summary>
        private readonly SortedSet<int> _indices = new SortedSet<int>();

        /// <summary>
        /// 一時的な選択（ドラッグ中など）
        /// </summary>
        public readonly HashSet<int> TempSelectedIndices = new HashSet<int>();

        public int Count => _indices.Count;
        public bool IsEmpty => _indices.Count == 0;
        public int? FirstOrDefault() => _indices.FirstOrDefault();
        public int? LastOrDefault() => _indices.LastOrDefault();

        public bool Add(int index) {
            return _indices.Add(index);
        }
        public bool Add(IEnumerable<int> indices) {
            bool wasChange = false;
            foreach (var idx in indices) {
                wasChange |= _indices.Add(idx);
            }
            return wasChange;
        }
        public bool Remove(int index) {
            return _indices.Remove(index);
        }
        public bool Remove(IEnumerable<int> indices) {
            bool wasChange = false;
            foreach (var idx in indices) {
                wasChange |= _indices.Remove(idx);
            }
            return wasChange;
        }
        public bool Select(int? index) {
            if (_indices.Count == 1 && _indices.First() == index) {
                return false;
            }
            SelectNone();
            if (index.HasValue) {
                _indices.Add(index.Value);
            }
            return true;
        }
        public bool Select(IEnumerable<int> indices) {
            SelectNone();
            foreach (var idx in indices) {
                _indices.Add(idx);
            }
            return true;
        }
        public bool Select(int start, int end) {
            if (start > end) {
                var tmp = start;
                start = end;
                end = tmp;
            }
            SelectNone();
            for (int i = start; i <= end; ++i) {
                _indices.Add(i);
            }
            return true;
        }
        public bool SelectAll(UCurve curve) {
            SelectNone();
            for (int i = 0; i < curve.xs.Count; ++i) {
                _indices.Add(i);
            }
            return true;
        }
        public bool SelectNone() {
            var ret = !IsEmpty;
            TempSelectedIndices.Clear();
            _indices.Clear();
            return ret;
        }
        public void SetTemporarySelection(IEnumerable<int> indices) {
            TempSelectedIndices.Clear();
            foreach (var idx in indices) {
                TempSelectedIndices.Add(idx);
            }
        }
        public void SetTemporarySelection(int start, int end) {
            TempSelectedIndices.Clear();
            if (start > end) {
                var tmp = start;
                start = end;
                end = tmp;
            }
            for (int i = start; i <= end; ++i) {
                TempSelectedIndices.Add(i);
            }
        }
        public void CommitTemporarySelection() {
            Add(TempSelectedIndices);
            TempSelectedIndices.Clear();
        }
        public List<int> ToList() {
            return _indices.ToList();
        }
        public int[] ToArray() {
            return _indices.ToArray();
        }
        public IEnumerator<int> GetEnumerator() {
            return _indices.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable)_indices).GetEnumerator();
        }

        // 範囲選択
        public bool SelectRange(int start, int end) {
            return Select(start, end);
        }

        // 選択リセット
        public bool ResetSelection() {
            return SelectNone();
        }

        // 上下移動（選択インデックスのy値を±deltaだけ移動、UCurve参照が必要）
        public bool MoveSelectionY(UCurve curve, int delta) {
            if (IsEmpty) return false;
            foreach (var idx in _indices) {
                if (idx < 0 || idx >= curve.ys.Count) continue;
                curve.ys[idx] = Math.Clamp(curve.ys[idx] + delta, (int)curve.descriptor.min, (int)curve.descriptor.max);
            }
            return true;
        }

        // 左右移動（選択範囲全体を±deltaだけインデックスごと移動）
        public bool MoveSelectionX(int delta, int maxIndex) {
            if (IsEmpty) return false;
            int start = _indices.First();
            int end = _indices.Last();
            int newStart = start + delta;
            int newEnd = end + delta;
            if (newStart < 0 || newEnd > maxIndex) return false;
            Select(newStart, newEnd);
            return true;
        }

        // 反転（選択範囲のインデックス順を逆転）
        public bool InvertSelection(int maxIndex) {
            if (IsEmpty) return false;
            int start = _indices.First();
            int end = _indices.Last();
            var inverted = Enumerable.Range(start, end - start + 1).Reverse();
            SelectNone();
            foreach (var idx in inverted) {
                _indices.Add(idx);
            }
            return true;
        }

        // 上下拡大縮小（選択範囲のy値を中心から拡大/縮小、UCurve参照が必要）
        public bool ScaleSelectionY(UCurve curve, double scale) {
            if (IsEmpty) return false;
            // 中心値を基準に拡大縮小
            double center = _indices.Select(idx => curve.ys[idx]).Average();
            foreach (var idx in _indices) {
                if (idx < 0 || idx >= curve.ys.Count) continue;
                double newY = center + (curve.ys[idx] - center) * scale;
                curve.ys[idx] = (int)Math.Clamp(newY, curve.descriptor.min, curve.descriptor.max);
            }
            return true;
        }

        // 左右拡大縮小（選択範囲のインデックスを中心から拡大/縮小）
        public bool ScaleSelectionX(int maxIndex, double scale) {
            if (IsEmpty) return false;
            int start = _indices.First();
            int end = _indices.Last();
            double center = (start + end) / 2.0;
            int count = end - start + 1;
            var newIndices = new HashSet<int>();
            foreach (var idx in _indices) {
                double offset = idx - center;
                int newIdx = (int)Math.Round(center + offset * scale);
                if (newIdx >= 0 && newIdx <= maxIndex) {
                    newIndices.Add(newIdx);
                }
            }
            if (newIndices.Count == 0) return false;
            SelectNone();
            foreach (var idx in newIndices.OrderBy(i => i)) {
                _indices.Add(idx);
            }
            return true;
        }
    }
}
