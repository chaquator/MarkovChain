﻿using System;
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
			const int sequentialThreshold = 2048;

			if (right > left) {
				if (right - left < sequentialThreshold) {
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
		/// <param name="sortedItems">List of items, sorted in order from highest
		/// to lowest weight</param>
		/// <param name="runningTotals">List which corresponds with running
		/// total of the items weight so far. The final element of this list
		/// will be the total weight of the construct</param>
		/// <param name="weightSelector">Function which returns the weight
		/// associated with the item</param>
		/// <param name="random">Random number generator class instance to make use of</param>
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
		/// Func&lt;char, int&gt; selector = c =&gt; m[c];
		/// 
		/// Random rand = new Random();
		/// 
		/// char result = RandomWeightedChoice(k, r, selector, rand);
		/// 
		/// Console.Write(result);
		/// </code>
		/// </example>
		public static T RandomWeightedChoice<T>(T[] sortedItems,
			int[] runningTotals,
			Func<T, int> weightSelector,
			Random random) {
			if (sortedItems.Length == 1) return sortedItems[0];

			int target = random.Next(runningTotals[runningTotals.Length - 1]);
			int current = 0;

			while (runningTotals[current] < target) {
				// Hop is based on remaining distance divided by the weight
				// of the current
				current += 1 + ((target - runningTotals[current] - 1) /
							weightSelector(sortedItems[current]));
			}

			return sortedItems[current];
		}

		// https://stackoverflow.com/a/3635650/4535218
		public static int GCD(IEnumerable<int> numbers) {
			return numbers.Aggregate(GCD);
		}

		public static int GCD(int a, int b) {
			return b == 0 ? a : (a == 0 ? b : GCD(Math.Min(a, b), Math.Max(a, b) % Math.Min(a, b)));
		}

		// Add in sorted manner to list
		public static void SortAdd<T>(this List<T> list, T add, IComparer<T> comp) {
			int bs = list.BinarySearch(add, comp);
			bs = (bs == -1) ? 0 : (bs < 0) ? ~bs : bs;

			list.Insert(bs, add);
		}
	}
}
