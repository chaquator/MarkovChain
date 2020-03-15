﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace MarkovChain.Structs {
	// TODO: change as many as necessary fields inside namespace to internal

	/// <summary>
	/// Master MarkovStructure, has associated dictioanry, array of links,
	/// and an array of indeces to links array which are all "starers"
	/// </summary>
	public class MarkovStructure {
		/// <summary>
		/// Dictionary is an array of strings, where each index is used as the reference in the n-grams
		/// </summary>
		internal readonly string[] dictionary;

		/// <summary>
		/// Array of all possible ngrams. Each ngram has a list of integers which correspond to
		/// the indeces of the corresponding word in the gram within the dictionary array.
		/// </summary>
		internal readonly NGram[] grams;

		/// <summary>
		/// Array which pairs ngrams with all their successors.
		/// Each chain link's index corresponds with the ngram it's associated with. e.g. chain_links[0] is paired with grams[0].
		/// </summary>
		internal readonly MarkovSegment[] chain_links;

		/// <summary>
		/// Array that points to a seed by its ngram index. A seed is an ngram which is at the start of a sentence.
		/// </summary>
		internal readonly int[] seeds;

		/// <summary>
		/// Generates a sequence of indeces which represent words from the
		/// dicitonary to be strung together
		/// </summary>
		/// <returns></returns>
		public int[] GenerateSqeuence(Random rand) {
			//	Start with sentence prototype as int list, curgram
			//	Get seed (will be uniform probability, for now at least)
			//	Set curgram to seed
			//	Copy ngram's contents to list
			//	Set curgram to random successor of seed
			//	While curgram's last index isn't -1 (end of sentence)
			//		Put curgram's last index to end of sentence
			//		Set curgram to random successor of curgram
			//	Return sentence as int array

			// Start with sentence prototype as int list, curgram
			List<int> proto_ret;
			int curgram_i;
			int[] curgram_l;

			// Get seed
			curgram_i = seeds[rand.Next(seeds.Length)];
			curgram_l = grams[curgram_i].gram;

			// Copy ngram's contents to list
			proto_ret = new List<int>(curgram_l);

			// Short circuit return for seeds which are a single sentence
			if (curgram_l[curgram_l.Length - 1] == -1) {
				proto_ret.RemoveAt(curgram_l.Length - 1);
				return proto_ret.ToArray();
			}

			// Set curgram to successor
			curgram_i = chain_links[chain_links[curgram_i].randomSuccessor().successor_index].current_ngram; // This needs some restructuring in the classes
			curgram_l = grams[curgram_i].gram;

			// While curgram's last index isn't -1 (end of sentence)
			while (curgram_l[curgram_l.Length - 1] != -1) {
				// Put curgram's last index to end of sentence
				proto_ret.Add(curgram_l[curgram_l.Length - 1]);

				// Set curgram to successor
				curgram_i = chain_links[curgram_i].randomSuccessor().successor_index;
				curgram_l = grams[curgram_i].gram;
			}

			// Sentence as int array
			return proto_ret.ToArray();
		}

		/// <summary>
		/// Converts a sequence of indeces representing words in the dictionary
		/// to a string
		/// </summary>
		/// <param name="seq"></param>
		/// <returns></returns>
		public string SequenceToString(int[] seq) {
			StringBuilder sb = new StringBuilder();

			for (int index = 0; index < seq.Length; ++index) {
				sb.Append(dictionary[seq[index]]);

				if (index + 1 < seq.Length) sb.Append(" ");
			}

			return sb.ToString();
		}

		/// <summary>
		/// Generates a sentence string
		/// </summary>
		/// <returns></returns>
		public string GenerateSentence(Random rand) {
			return SequenceToString(GenerateSqeuence(rand));
		}

		/// <summary>
		/// Writes structure to file in JSON format
		/// </summary>
		/// <param name="filename"></param>
		public void WriteFile(string filename) {
			const bool writeIndent = true;
			JsonWriterOptions jswropt = new JsonWriterOptions {
				Indented = writeIndent,
				SkipValidation = true
			};
			JsonSerializerOptions jssropt = new JsonSerializerOptions() {
				WriteIndented = writeIndent
			};

			using (FileStream fs = new FileStream(filename, FileMode.Create))
			using (Utf8JsonWriter jswr = new Utf8JsonWriter(fs, jswropt)) {
				Meta.MarkovStructureJsonConverter converter = new Meta.MarkovStructureJsonConverter();
				converter.Write(jswr, this, jssropt);
			}
		}

		/// <summary>
		/// Converts structure to JSON string
		/// </summary>
		/// <returns></returns>
		public override string ToString() {
			JsonSerializerOptions jssropt = new JsonSerializerOptions() {
				WriteIndented = false
			};
			jssropt.Converters.Add(new Meta.MarkovStructureJsonConverter());
			return JsonSerializer.Serialize<MarkovStructure>(this, jssropt);
		}

		/// <summary>
		/// Reads JSON file into structure
		/// </summary>
		/// <param name="filename"></param>
		/// <returns></returns>
		public static MarkovStructure ReadFile(string filename) {
			using (FileStream fs = new FileStream(filename, FileMode.Open)) {
				JsonSerializerOptions jssropt = new JsonSerializerOptions();
				jssropt.Converters.Add(new Meta.MarkovStructureJsonConverter());
				return JsonSerializer.DeserializeAsync<MarkovStructure>(fs, jssropt).Result;
			}
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
			// Population is doing such that chain link's index corresponds with ngram's index in master gram array
			chain_links = new MarkovSegment[grams.Length];
			Parallel.For(0, grams.Length,
				(ind) => {
					chain_links[ind] = new MarkovSegment(ind, prototype_chainlinks[ind]);
				}
			);

			// Populate list of seeds
			seeds = sds.Keys.ToArray();
		}

		public MarkovStructure(string[] dic, NGram[] grms, MarkovSegment[] links, int[] sds) {
			dictionary = dic;
			grams = grms;
			chain_links = links;
			seeds = sds;
		}

		public MarkovStructure combine(MarkovStructure other) {
			// TODO: finish design and write proper comments of this function

			//	What to do with dictionary
			//		Create a map for other dictionary which maps every index in other's dic to a index in combined dic
			//			All combined dic starts as current's dic
			//	Then ngrams
			//		All of own ngrams will not need to be changed
			//		Will need to go through other's ngrams and remake with new indeces
			//		Will need to make a combined list of ngrams, store the unique ones resulting from remap
			//		In summary, combined ngrams and combined ngrammap
			//	The chain links
			//		All of own links will need not be changed
			//		For the other, there are two cases based on associated ngram: unique to combined, not unique
			//			Case can be determined by index > own.grams.Length
			//		For unique, place at end sorted by associated index
			//			TODO: determine if GenerateSequence method needs to be changed to prevent strict need
			//		For non unique, combine markovsegments
			//			TODO: make combine method for markovsegments
			//	Then seeds
			//		Just combine seeds?
			//
			//	Create combined structures, populate with own
			//		Create combined dic & remap dic, populate both
			//		Create combined ngrams & remap, populate both
			//	Combine other's structures
			//		Dictionary - Go through other's dictionary and populate onto combined dictionary
			//			If dicmap does not have current from other then
			//				Insert current into combined dictionary and map current to corresponding index in dicmap
			//		Dic Remap - Create remap array which maps other's indeces to combined indeces
			//		Ngrams - Go through other's ngrams (current)
			//			Remap current ngram to new indeces
			//			If current remapped is not in ngrammap,
			//				Add to end of combined ngrams, set corresponding index in map

			// Combined dictionary, dicmap
			List<string> combined_dictionary = new List<string>(dictionary) {
				Capacity = dictionary.Length + other.dictionary.Length
			};
			Dictionary<string, int> combined_dicmap = new Dictionary<string, int>(dictionary.Length + other.dictionary.Length);

			// Combined ngrams, ngrammap
			List<NGram> combined_ngrams = new List<NGram>(grams) {
				Capacity = grams.Length + other.grams.Length
			};
			Dictionary<NGram, int> combined_ngrammap = new Dictionary<NGram, int>(grams.Length + other.grams.Length);

			Task populate_dicmap = Task.Run(() => {
				int i = 0;
				foreach (string w in dictionary) {
					combined_dicmap[w] = i++;
				}
			});

			Task populate_ngrammap = Task.Run(() => {
				int i = 0;
				foreach (NGram gram in grams) {
					combined_ngrammap[gram] = i++;
				}
			});

			Task.WaitAll(populate_dicmap, populate_ngrammap);

			// TODO: evaulate whether all this concurrency performs better

			// Go through other's dictionary, populate onto combined
			foreach (string w in other.dictionary) {
				if (!combined_dicmap.ContainsKey(w)) {
					combined_dicmap[w] = combined_dictionary.Count;
					combined_dictionary.Add(w);
				}
			}

			// Remap array that maps other's index to combined index (remap[i] = j where other[i] = combined[j])
			int[] other_dic_remap = new int[other.dictionary.Length];
			Parallel.For(0, other_dic_remap.Length, (index) => {
				other_dic_remap[index] = combined_dicmap[other.dictionary[index]];
			});

			// Go through other's ngrams, populate onto combined
			foreach (NGram gram in other.grams) {
				// Remap indeces in current gram to combined
				int[] g = gram.gram.Select((e) => other_dic_remap[e]).ToArray();

				// Create new ngram with this remap
				NGram remap = new NGram(g);
				if (!combined_ngrammap.ContainsKey(gram)) {
					combined_ngrammap[gram] = combined_ngrams.Count;
					combined_ngrams.Add(gram);
				}
			}

			// Create other ngram remap
			int[] other_ngram_remap = new int[other.grams.Length];
			Parallel.For(0, other_dic_remap.Length, (index) => {
				other_ngram_remap[index] = combined_ngrammap[other.grams[index]];
			});

			// TODO: finish the rest of combine function

			return null;
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
		internal readonly int current_ngram;

		/// <summary>
		/// Sorted array of ngrams which succeed given ngram, along with their relatively frequency
		/// </summary>
		internal readonly NGramSuccessor[] successors;

		/// <summary>
		/// Array which is used for random selection which contains running total of weights
		/// for each index in the successors
		/// </summary>
		internal readonly int[] runningTotal;

		/// <summary>
		/// Selects random successor paying attention to weight
		/// </summary>
		/// <remarks>Hopscotch selection from https://blog.bruce-hill.com/a-faster-weighted-random-choice </remarks>
		/// <returns>Returns a chain link representing the successor</returns>
		public NGramSuccessor randomSuccessor() {
			return Utils.RandomWeightedChoice(successors, runningTotal, (x) => x.weight);
		}

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
			List<NGramSuccessor> sucset = new List<NGramSuccessor>(prototype_successors.Count);

			// Add successors in sorted form
			NGramSuccessor add;
			int bs; // index to binary search for
			foreach (var successor in prototype_successors) {
				add = new NGramSuccessor(successor.Key, successor.Value);

				bs = sucset.BinarySearch(add, new Meta.ReverseNGramSuccessorComparer());
				bs = (bs == -1) ? 0 : (bs < 0) ? ~bs : bs;

				sucset.Insert(bs, add);
			}

			successors = sucset.ToArray();

			// Running totals
			int total_weight = 0;
			runningTotal = new int[prototype_successors.Count];
			for (int ind = 0; ind < runningTotal.Length; ++ind) {
				total_weight += successors[ind].weight;
				runningTotal[ind] = total_weight;
			}
		}

		// Precondition: successors are sorted highest weight to lowest,
		// Running totals array is also correct
		public MarkovSegment(int ngram, NGramSuccessor[] sucs, int[] runtot) {
			current_ngram = ngram;
			successors = sucs;
			runningTotal = runtot;
		}

		public MarkovSegment combine(MarkovSegment other, int[] other_ngram_remap) {
			// TODO: complete combine method for markovsegment
			//	Discard running totals
			//	Use own's successors as base
			//	(we need to figure out how to deal with duplicates by index, they may not be the same weight)
			//	Rebuild running totals list
			return null;
		}
	}

	/// <summary>
	/// Successor struct, couples ngram
	/// </summary>
	public struct NGramSuccessor : IComparable<NGramSuccessor> {
		/// <summary>
		/// Index points to associated MarkovStructure chain_links index
		/// </summary>
		public readonly int successor_index;

		/// <summary>
		/// Weight whos magnitude reflects relative frequency of successor
		/// </summary>
		public readonly int weight;

		public NGramSuccessor(int suc, int w) {
			successor_index = suc;
			weight = w;
		}

		int IComparable<NGramSuccessor>.CompareTo(NGramSuccessor o) {
			return weight.CompareTo(o.weight);
		}
	}

	/// <summary>
	/// Individual ngram
	/// </summary>
	public struct NGram {
		/// <summary>
		/// Array of indeces which correspond to words in dictionary
		/// </summary>
		public readonly int[] gram;

		public NGram(int[] g) {
			gram = g;
		}

		public override bool Equals(object obj) {
			if ((obj == null) || !GetType().Equals(obj.GetType())) {
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

				return gram.Aggregate(SEED, (hash, field) => (LARGEPRIME * hash ^ field.GetHashCode()));
			}
		}

		public override string ToString() {
			return String.Join(" ", gram);
		}
	}

}
