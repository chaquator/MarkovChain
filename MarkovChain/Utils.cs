using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarkovChain {
	public static class Utils {
		/// <summary>
		/// Sweep over text
		/// </summary>
		/// <param name="Text"></param>
		/// <returns></returns>
		/// <remarks>https://stackoverflow.com/a/1443004</remarks>
		public static IEnumerable<string> WordList(this string Text, char token = ' ') {
			int cIndex = 0;
			int nIndex;
			while ((nIndex = Text.IndexOf(token, cIndex + 1)) != -1) {
				int sIndex = (cIndex == 0 ? 0 : cIndex + 1);
				yield return Text.Substring(sIndex, nIndex - sIndex);
				cIndex = nIndex;
			}
			yield return Text.Substring(cIndex + 1);
		}

		public static int Partition<T>(T[] arr, int left, int right) where T : IComparable<T> {
			// Pick starting pivot (middle prevents stack overflow for sorted array)
			int med = left + (right - left) / 2;
			int l = left;

			// Swap with left
			T pivot = arr[med];
			arr[med] = arr[left];
			arr[left] = pivot;

			++left;
			T temp;
			while (left <= right) {
				// Move fingers
				while (arr[left].CompareTo(pivot) < 0) ++left;
				while (arr[right].CompareTo(pivot) > 0) --right;

				// Swap
				if (left <= right) {
					temp = arr[left];
					arr[left] = arr[right];
					arr[right] = temp;
				}
			}

			// TODO: is right always the correct answer??
			// Move pivot
			arr[l] = arr[right];
			arr[right] = pivot;

			return right;
		}

		// https://web.archive.org/web/20120201062954/http://www.darkside.co.za/archive/2008/03/14/microsoft-parallel-extensions-.net-framework.aspx
		public static void QuicksortParallelOptimised<T>(T[] arr, int left, int right) where T : IComparable<T> {
			const int SEQUENTIAL_THRESHOLD = 2048;

			if (right > left) {
				if (right - left < SEQUENTIAL_THRESHOLD) {
					Array.Sort(arr, left, right - left);
				} else {
					int pivot = Partition(arr, left, right);
					Parallel.Invoke(
						() => QuicksortParallelOptimised(arr, left, pivot - 1),
						() => QuicksortParallelOptimised(arr, pivot + 1, right));
				}
			}
		}


		/// <summary>
		/// Returns random choice from a list with consideration for the weight
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="sorted_items">List of items, sorted in order from highest
		/// to lowest weight</param>
		/// <param name="running_totals">List which corresponds with running
		/// total of the items weight so far. The final element of this list
		/// will be the total weight of the construct</param>
		/// <param name="weight_selector">Function which returns the weight
		/// associated with the item</param>
		/// <remarks>Hopscotch selection retrieved from
		/// https://blog.bruce-hill.com/a-faster-weighted-random-choice </remarks>
		/// <returns>Choice which has been selected</returns>
		/// <example>
		/// <code>
		/// // Map assocaiting character with relative frequency
		/// Dictionary&lt;char, int&gt; m = new Dictionary&lt;char, int&gt; {
		/// 	{ 'a', 5 },
		/// 	{ 'b', 2 },
		/// 	{ 'c', 2 },
		/// 	{ 'd', 2 },
		/// 	{ 'e', 1 }
		/// };
		/// 
		/// // Building running totals list
		/// int[] r = new int[m.Count];
		/// int t = 0;
		/// int i = 0;
		/// foreach (var kvp in m) {
		/// 	t += kvp.Value;
		/// 	r[i++] = t;
		/// }
		/// 
		/// char[] k = new char[m.Count]; // the array of characters
		/// m.Keys.CopyTo(k, 0);
		/// 
		/// // Weight selector function, uses map
		/// Func&lt;char, int&gt; selector = (c) =&gt; m[c];
		/// 
		/// char result = Utils.RandomWeightedChoice(k, r, selector)
		/// 
		/// Console.Write(result);
		/// </code>
		/// </example>
		public static T RandomWeightedChoice<T>(T[] sorted_items,
			int[] running_totals,
			Func<T, int> weight_selector) {
			if (sorted_items.Length == 1) return sorted_items[0];

			Random r = new Random();
			int target = r.Next(running_totals[running_totals.Length - 1]);
			int current = 0;

			while(running_totals[current] < target) {
				// Hop is based on remaining distance divided by the weight
				// of the current
				current += 1 + ((target - running_totals[current] - 1) /
							weight_selector(sorted_items[current]));
			}

			return sorted_items[current];
		}
	}
}
