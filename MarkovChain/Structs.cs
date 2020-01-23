﻿using System;
using System.Linq;
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
			private NGram[] grams;

			/// <summary>
			/// Array of all unique ngrams, each with their successors
			/// </summary>
			private MarkovSegment[] chain_links;

			/// <summary>
			/// Array of indeces which point to chain links that happen to be starts of sentences
			/// </summary>
			private int[] seeds;

			// TODO: WRITE
			private int[] GenerateSqeuence() {
				return new int[] { 0 };
			}

			// TODO: write
			private string SequenceToString(int[] seq) {
				return "";
			}

			// TODO: summary
			public string GenerateSentence() {
				return SequenceToString(GenerateSqeuence());
			}

			// TODO: WRITE
			public void WriteFile(string filename) {
				// Not gonna need to write out dictioanry, only used for creating
			}

			/// <summary>
			/// Constructor for a markovstructure, only to be used by pipeline
			/// </summary>
			/// <param name="dic">Master dictionary, array of words</param>
			/// <param name="grms">Thread-safe master list of ngrams</param>
			/// <param name="prototype_chainlinks">Prototype of chain links, maps ngram-index
			/// to prototype of successors (which maps succeeding index to weight)</param>
			/// <param name="sds">Prototype of seed list, in hash map form for quick access</param>
			public MarkovStructure(string[] dic, ConcurrentQueue<NGram> grms,
								ConcurrentDictionary<int, ConcurrentDictionary<int, int>> prototype_chainlinks,
								ConcurrentDictionary<int, bool> sds) {
				// Pass along master dictionary
				dictionary = dic;

				// Populate master grams table 
				grams = grms.ToArray();

				// Populate chain links
				chain_links = new MarkovSegment[grams.Length];
				Parallel.For(0, grams.Length,
					(ind) => {
						chain_links[ind] = new MarkovSegment(ind, prototype_chainlinks[ind]);
					}
				);

#if false
				int ind = 0;
				foreach (var link in successor_prototype) {
					// key is index of current ngram
					// value is map<index, weight> -- all successors of given index
					chain_links[ind++] = new MarkovSegment(link);
				}
#endif

				// Populate list of seeds
				seeds = sds.Keys.ToArray();
			}
		}

		/// <summary>
		/// Single segment in overall MarkovStructure, used in tandem with master
		/// array to assemble sentence
		/// </summary>
		class MarkovSegment {
			/// <summary>
			/// Index which points to associated ngram in master
			/// markov structure
			/// </summary>
			public int current_ngram { get; }

			/// <summary>
			/// Array of ngrams which succeed given ngram, along with their relatively frequency
			/// </summary>
			public NGramSuccessor[] successors { get; }

			/// <summary>
			/// Constructor of MarkovSegment, meant to be used by MarkovStructure
			/// </summary>
			/// <param name="ngram"></param>
			/// <param name="prototype_successors"></param>
			public MarkovSegment(int ngram, ConcurrentDictionary<int, int> prototype_successors) {
				// key is index of current ngram
				// value is map<index of successor, associated weight>
				current_ngram = ngram;

				// Populate successors
				successors = new NGramSuccessor[prototype_successors.Count];
				int ind = 0;
				foreach (var successor in prototype_successors) {
					successors[ind++] = new NGramSuccessor(successor.Key, successor.Value);
				}
			}
		}

		/// <summary>
		/// Successor struct, couples ngram
		/// </summary>
		class NGramSuccessor {
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

					// Compare by array value, not by reference or whatever C# does by default
					return Enumerable.SequenceEqual(gram, o.gram);
				}
			}

			public override int GetHashCode() {
				unchecked {
					int SEED = (int)2509506049;
					int LARGEPRIME = (int)4134118063;
					/* FOLLOWING LINQ IS EQUIVALENT TO
						int hash = SEED;
						foreach (var field in gram) {
							hash = LARGEPRIME * hash ^ field.GetHashCode();
						}
						return hash;
					*/
					return gram.Aggregate(SEED, (hash, field) => (LARGEPRIME * hash ^ field.GetHashCode()));
				}
			}

			public override string ToString() {
				return String.Join(" ", gram);
			}
		}
	}
}
