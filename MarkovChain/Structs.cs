using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

using System.IO;
using System.Text;
using System.Text.Json;

using System.Diagnostics;


namespace MarkovChain.Structs {
	// TODO: within constructors of all objects, imeplement checks on valid data known by previous data (e.g. dictionary size)

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
		private int[] GenerateSqeuence(Random rand) {
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
			curgram_i = chain_links[curgram_i].randomSuccessor().successor_index;
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
		private string SequenceToString(int[] seq) {
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

		public MarkovStructure Combine(MarkovStructure other) {
			// TOOD: add summary, create unit test

			// --- Dictionary combining

			// Combined dictionary, dicmap
			List<string> combined_dictionary = new List<string>(dictionary) {
				Capacity = dictionary.Length + other.dictionary.Length
			};
			Dictionary<string, int> dicmap = new Dictionary<string, int>(dictionary.Length + other.dictionary.Length);

			// Populate dicmap
			int i = 0;
			foreach (string w in dictionary) {
				dicmap[w] = i++;
			}

			// Go through other's dictionary, populate onto combined
			foreach (string w in other.dictionary) {
				if (!dicmap.ContainsKey(w)) {
					dicmap[w] = combined_dictionary.Count;
					combined_dictionary.Add(w);
				}
			}

			// Remap array that maps other's index to combined index (remap[i] = j where other[i] = combined[j])
			int[] dic_remap = new int[other.dictionary.Length];
			for (int index = 0; index < dic_remap.Length; ++index) {
				string others_cur_word = other.dictionary[index];
				dic_remap[index] = dicmap[others_cur_word];
			}

			// --- NGram Combining

			// TODO: it's possible to combine ngrams and their links at the same time instead of doing more work

			// Combined ngrams, ngrammap
			List<NGram> combined_ngrams = new List<NGram>(grams) {
				Capacity = grams.Length + other.grams.Length
			};
			Dictionary<NGram, int> ngrammap = new Dictionary<NGram, int>(grams.Length + other.grams.Length);

			// Populate gram map with own grams
			i = 0;
			foreach (NGram gram in grams) {
				ngrammap[gram] = i++;
			}

			// Go through other's ngrams, populate onto combined, and populate ngram remap
			i = 0;
			int[] ngram_remap = new int[other.grams.Length];
			// TODO: consider parallelizing, would involve an add queue and a lock potentially
			foreach (NGram gram in other.grams) {
				// Translate ngram using dictionary remap
				int[] g = gram.gram.Select((e) => (e == -1) ? -1 : dic_remap[e]).ToArray();
				NGram remap = new NGram(g);

				if (ngrammap.TryGetValue(remap, out int index)) {
					// If remapped ngram is not unique, remap points to it in combined
					ngram_remap[i++] = index;
				} else {
					// If translated ngram is unique, add it to the end, remap points to it
					ngram_remap[i++] = combined_ngrams.Count;
					combined_ngrams.Add(remap);
				}
			}

			// --- Chain links combining

			//	Other's unique chain links will not need to be touched
			//		Can tell if it's unique by testing whether ngram remap index >= original.length
			//		Remember that ngrams and the links are associated together despite being in seperate arrays (i.e. ngram[0] corresponds with links[0])
			//	For those which need to be comebined, use MarkovSegment combine method

			MarkovSegment[] combined_links = new MarkovSegment[combined_ngrams.Count];

			// Populate combined_links with own
			Parallel.For(0, combined_links.Length, (index) => {
				combined_links[index] = chain_links[index];
			});

			// Populate linkmap with other
			// TODO: make parallel when done testing
			// Parallel.For(0, other.chain_links.Length, (index) => {
			for (int index = 0; index < other.chain_links.Length; ++index) {
				MarkovSegment other_seg = other.chain_links[index];

				int remap;
				if ((remap = ngram_remap[index]) >= chain_links.Length) {
					// Unique link needs to be associated with its remap spot
					combined_links[remap] = other_seg;
				} else {
					MarkovSegment own_seg = chain_links[remap];
					// Otherwise, combine the segments and replace
					MarkovSegment replace = own_seg.Combine(other_seg, ngram_remap, grams.Length);

					// Replace link in relevant structures
					combined_links[remap] = replace;
				}
			}
			// });

			// TODO: remove when done testing
			if (combined_links.Contains(null)) {
				Console.WriteLine("yeah crazy");
			}

			// --- Seed combining

			//	Run the other's seeds through ngram remap,
			//	Any of other's seeds which are unique (larger than original seed's length), add to end

			List<int> combined_seeds = new List<int>(seeds) {
				Capacity = seeds.Length + other.seeds.Length
			};

			combined_seeds.AddRange(from oseed in other.seeds
									where ngram_remap[oseed] >= seeds.Length
									select oseed);

			// Put it all together
			return new MarkovStructure(combined_dictionary.ToArray(),
										combined_ngrams.ToArray(),
										combined_links,
										combined_seeds.ToArray());
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
					chain_links[ind] = new MarkovSegment(prototype_chainlinks[ind]);
				}
			);

			// Populate list of seeds
			seeds = sds.Keys.ToArray();
		}

		internal MarkovStructure(string[] dic, NGram[] grms, MarkovSegment[] links, int[] sds) {
			dictionary = dic;
			grams = grms;
			chain_links = links;
			seeds = sds;
		}
	}

	/// <summary>
	/// Single segment in overall MarkovStructure, used in tandem with master
	/// array to assemble sentence
	/// </summary>
	internal class MarkovSegment {
		/// <summary>
		/// Sorted array of ngrams which succeed given ngram, along with their relatively frequency
		/// </summary>
		internal NGramSuccessor[] successors;

		/// <summary>
		/// Array which is used for random selection which contains running total of weights
		/// for each index in the successors
		/// </summary>
		internal int[] runningTotal;

		/// <summary>
		/// Selects random successor paying attention to weight
		/// </summary>
		/// <remarks>Hopscotch selection from https://blog.bruce-hill.com/a-faster-weighted-random-choice </remarks>
		/// <returns>Returns a chain link representing the successor</returns>
		internal NGramSuccessor randomSuccessor() {
			return Utils.RandomWeightedChoice(successors, runningTotal, (x) => x.weight);
		}

		// Setup function which will normalize successors and compute running totals
		private void _Setup(IEnumerable<NGramSuccessor> sucs) {
			// TODO: stop going crazy with linq just do things properly

			// Get GDC of weights
			int GDC = Utils.GCD(sucs.Select<NGramSuccessor, int>(e => e.weight));

			// Successors are divided by GDC
			successors = sucs.Select(e => new NGramSuccessor(e.successor_index, e.weight/GDC)).ToArray();

			// Compute running total
			int total_weight = 0;
			int[] runningTotal = new int[successors.Length];
			for (int ind = 0; ind < runningTotal.Length; ++ind) {
				total_weight += successors[ind].weight;
				runningTotal[ind] = total_weight;
			}
		}

		/// <summary>
		/// Constructor of MarkovSegment, meant to be used by MarkovStructure
		/// </summary>
		/// <param name="prototype_successors"></param>
		internal MarkovSegment(ConcurrentDictionary<int, int> prototype_successors) {
			// key is index of current ngram
			// value is map<index of successor, associated weight>

			// Populate successors
			List<NGramSuccessor> sucset = new List<NGramSuccessor>(prototype_successors.Count);

			// Add successors in sorted form
			NGramSuccessor.ReverseComparer rev = new NGramSuccessor.ReverseComparer();
			foreach (var successor in prototype_successors) sucset.SortAdd(new NGramSuccessor(successor.Key, successor.Value), rev);

			_Setup(sucset.ToArray());
		}

		// For constructing from combine function
		internal MarkovSegment(IEnumerable<NGramSuccessor> sucs) {
			_Setup(sucs);
		}

		// For constructing from reading from file
		// Prerequesites: successors are sorted from highest weight to lowest weight, running total accurately reflect weight
		internal MarkovSegment(NGramSuccessor[] sucs, int[] runtot) {
			successors = sucs;
			runningTotal = runtot;
		}

		// TODO: add summary, create unit test
		public MarkovSegment Combine(MarkovSegment other, int[] ngram_remap, int own_ngram_length) {
			if (successors.Length == 0) return other;
			if (other.successors.Length == 0) return this;

			// Combined list, map
			// TODO: consider switching to BST structure (likely SortedSet) to prevent O(n) of list insert???
			List<NGramSuccessor> combined_successors = new List<NGramSuccessor>(successors) {
				Capacity = successors.Length + other.successors.Length
			};
			Dictionary<int, int> sucmap = new Dictionary<int, int>(successors.Length + other.successors.Length);

			// Populate map with own
			int ind = 0;
			foreach (NGramSuccessor suc in successors) {
				// TODO: i dont think it should happen but each entry in here should be unique, maybe some sort of testing whether sucmap already has the index
				sucmap[suc.successor_index] = ind++;
			}
			
			NGramSuccessor.ReverseComparer rev = new NGramSuccessor.ReverseComparer();

			// Combine with other
			foreach (NGramSuccessor other_suc in other.successors) {
				int remap = ngram_remap[other_suc.successor_index];

				if (remap < own_ngram_length && sucmap.TryGetValue(remap, out int index)) {
					// Given succeeding gram is not unique to other, and within the own, succeeded the current ngram
					// Combine the weights basically

					// TODO: really wanna not have O(n) but idk
					// First, grab the relevant successor and remove
					NGramSuccessor own_suc = combined_successors[index];
					combined_successors.RemoveAt(index);

					// Combine weights
					own_suc.weight += other_suc.weight;

					// And add back (in sorted position)
					combined_successors.SortAdd(own_suc, rev);
				} else {
					// Either NGram is straight up unique to other, or the ngram is simply not a successor in this particular link
					combined_successors.SortAdd(new NGramSuccessor(remap, other_suc.weight), rev);
				}
			}

			return new MarkovSegment(combined_successors);
		}
	}

	[DebuggerDisplay("S: {successor_index} W: {weight}")]
	/// <summary>
	/// Successor struct, couples ngram
	/// </summary>
	internal struct NGramSuccessor : IComparable<NGramSuccessor> {
		/// <summary>
		/// Index points to associated MarkovStructure ngram/chain_link index
		/// </summary>
		internal readonly int successor_index;

		/// <summary>
		/// Weight whos magnitude reflects relative frequency of successor
		/// </summary>
		internal int weight;

		internal NGramSuccessor(int suc, int w) {
			successor_index = suc;
			weight = w;
		}

		int IComparable<NGramSuccessor>.CompareTo(NGramSuccessor o) {
			return weight.CompareTo(o.weight);
		}

		internal class ReverseComparer : IComparer<NGramSuccessor> {
			public int Compare(NGramSuccessor x, NGramSuccessor y) {
				return y.weight.CompareTo(x.weight);
				// return (x.weight > y.weight) ? -1 : 1;
			}
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
