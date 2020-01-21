using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace MarkovChain {
	namespace Structs {
		/// <summary>
		/// Master MarkovStructure, has associated dictioanry, array of links,
		/// and an array of indeces to links array which are all "starers"
		/// </summary>
		public class MarkovStructure {
			/// <summary>
			/// Dictionary is an array of strings, where each index is used as the reference in the n-grams
			/// </summary>
			public string[] dictionary { get; }

			/// <summary>
			/// Grams is an array of all possible unqiue grams on their own
			/// </summary>
			public NGram[] grams { get; }

			/// <summary>
			/// Array of all unique ngrams, each with their successors
			/// </summary>
			public MarkovSegment[] chain_links { get; }

			/// <summary>
			/// Array of indeces which point to chain links that happen to be starts of sentences
			/// </summary>
			public int[] seeds { get; }

			// TODO: WRITE
			private int[] GenerateSqeuence() {
				return new int[] { 0 };
			}

			// TODO: WRITE
			public string GenerateSentence() {
				return "";
			}

			// TODO: WRITE
			public void WriteFile(string filename) {
				// Not gonna need to write out dictioanry, only used for creating
			}

			// TODO: TEST
			public MarkovStructure(string[] dic, ConcurrentQueue<NGram> grms,
								ConcurrentDictionary<int, ConcurrentDictionary<int, int>> prototype) {
				// Pass along master dictionary
				dictionary = dic;

				// Populate master grams table and chain links
				grams = grms.ToArray();

				// Populate list of seeds
				chain_links = new MarkovSegment[grams.Length];
				int ind = 0;
				foreach (var link in prototype) {
					// key is index of current ngram
					// value is map<index, weight> -- all successors of given index
					chain_links[ind] = new MarkovSegment(link);
					++ind;
				}
			}
		}

		/// <summary>
		/// Single segment in overall MarkovStructure, used in tandem with master
		/// array to assemble sentence
		/// </summary>
		public class MarkovSegment {
			/// <summary>
			/// Index which points to associated ngram in master
			/// markov structure
			/// </summary>
			public int current_ngram { get; }

			/// <summary>
			/// Array of ngrams which succeed given ngram, along with their relatively frequency
			/// </summary>
			public NGramSuccessor[] successors { get; }

			public MarkovSegment(KeyValuePair<int, ConcurrentDictionary<int, int>> prototype) {
				// key is index of current ngram
				// value is map<index of successor, associated weight>
				current_ngram = prototype.Key;

				// Populate successors
				successors = new NGramSuccessor[prototype.Value.Count];
				int ind = 0;
				foreach (var successor in prototype.Value) {
					successors[ind++] = new NGramSuccessor(successor.Key, successor.Value);
				}
			}
		}

		/// <summary>
		/// Successor struct, couples ngram
		/// </summary>
		public class NGramSuccessor {
			/// <summary>
			/// Index points to associated MarkovStructure chain_links index
			/// </summary>
			public int successor_index { get; }

			/// <summary>
			/// Weight whos magnitude reflects relative frequency of successor
			/// </summary>
			public int weight { get; }

			public NGramSuccessor(int suc, int w) {
				successor_index = suc;
				weight = w;
			}
		}

		/// <summary>
		/// Individual ngram
		/// </summary>
		public class NGram {
			/// <summary>
			/// Array of indeces which correspond to words in dictionary
			/// </summary>
			public int[] gram { get; }

			public NGram(int[] g) {
				gram = g;
			}

			public override bool Equals(object obj) {
				if ((obj == null) || !this.GetType().Equals(obj.GetType())) {
					return false;
				} else {
					NGram o = (NGram)obj;
					return Enumerable.SequenceEqual(gram, o.gram);
				}
			}

			public override int GetHashCode() {
				unchecked {
					int hash = (int)2509506049;
					int LARGEPRIME = (int)4134118063;
					foreach (var field in gram) {
						hash = LARGEPRIME * hash ^ field.GetHashCode();
					}
					return hash;
				}
			}
		}
	}
}
