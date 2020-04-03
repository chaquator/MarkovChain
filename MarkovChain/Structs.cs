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
		internal readonly MarkovSegment[] chainLinks;

		/// <summary>
		/// Array that points to a seed by its ngram index. A seed is an ngram which is at the start of a sentence.
		/// </summary>
		internal readonly int[] seeds;

		/// <summary>
		/// Generates a sequence of indeces which represent words from the
		/// dicitonary to be strung together
		/// </summary>
		/// <returns></returns>
		private IEnumerable<int> GenerateSqeuence(Random random) {
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
			List<int> protoReturn;
			int currentGramIndex;
			int[] currentGram;

			// Get seed
			currentGramIndex = seeds[random.Next(seeds.Length)];
			currentGram = grams[currentGramIndex].gram;

			// Copy ngram's contents to list
			protoReturn = new List<int>(currentGram);

			// Short circuit return for seeds which are a single sentence
			if (currentGram[currentGram.Length - 1] == -1) {
				protoReturn.RemoveAt(currentGram.Length - 1);
				return protoReturn;
			}

			// Set curgram to successor
			currentGramIndex = chainLinks[currentGramIndex].RandomSuccessor(random).successorIndex;
			currentGram = grams[currentGramIndex].gram;

			// While curgram's last index isn't -1 (end of sentence)
			while (currentGram[currentGram.Length - 1] != -1) {
				// Put curgram's last index to end of sentence
				protoReturn.Add(currentGram[currentGram.Length - 1]);

				// Set curgram to successor
				currentGramIndex = chainLinks[currentGramIndex].RandomSuccessor(random).successorIndex;
				currentGram = grams[currentGramIndex].gram;
			}

			// Sentence as int array
			return protoReturn;
		}

		/// <summary>
		/// Converts a sequence of indeces representing words in the dictionary
		/// to a string
		/// </summary>
		/// <param name="sequence"></param>
		/// <returns></returns>
		private string SequenceToString(IEnumerable<int> sequence) {
			return String.Join(" ", sequence.Select(e => dictionary[e]));
		}

		/// <summary>
		/// Generates a sentence string
		/// </summary>
		/// <returns></returns>
		public string GenerateSentence(Random random) {
			return SequenceToString(GenerateSqeuence(random));
		}

		/// <summary>
		/// Reads JSON file into structure
		/// </summary>
		/// <param name="filename"></param>
		/// <returns></returns>
		public static MarkovStructure ReadFile(string filename) {
			using (var filestream = new FileStream(filename, FileMode.Open)) {
				var jsonSerializerOptions = new JsonSerializerOptions();
				jsonSerializerOptions.Converters.Add(new Meta.MarkovStructureJsonConverter());
				return JsonSerializer.DeserializeAsync<MarkovStructure>(filestream, jsonSerializerOptions).Result;
			}
		}

		/// <summary>
		/// Writes structure to file in JSON format
		/// </summary>
		/// <param name="filename"></param>
		public void WriteFile(string filename) {
			bool writeIndent = true;
			var jsonWriterOptions = new JsonWriterOptions {
				Indented = writeIndent,
				SkipValidation = true
			};
			var jsonSerializerOptions = new JsonSerializerOptions() {
				WriteIndented = writeIndent
			};

			using (var filestream = new FileStream(filename, FileMode.Create))
			using (var jsonWriter = new Utf8JsonWriter(filestream, jsonWriterOptions)) {
				var converter = new Meta.MarkovStructureJsonConverter();
				converter.Write(jsonWriter, this, jsonSerializerOptions);
			}
		}

		/// <summary>
		/// Converts structure to JSON string
		/// </summary>
		/// <returns></returns>
		public override string ToString() {
			var jsonSerializerOptions = new JsonSerializerOptions() {
				WriteIndented = false
			};
			jsonSerializerOptions.Converters.Add(new Meta.MarkovStructureJsonConverter());
			return JsonSerializer.Serialize<MarkovStructure>(this, jsonSerializerOptions);
		}

		public MarkovStructure Combine(MarkovStructure other) {
			// TOOD: add summary, create unit test

			// --- Dictionary combining

			// Combined dictionary, dicmap
			List<string> combinedDictionary = new List<string>(dictionary) {
				Capacity = dictionary.Length + other.dictionary.Length
			};
			Dictionary<string, int> dictionaryMap = new Dictionary<string, int>(dictionary.Length + other.dictionary.Length);

			// Populate dictionaryMap
			int i = 0;
			foreach (string w in dictionary) {
				dictionaryMap[w] = i++;
			}

			// Go through other's dictionary, populate onto combined
			foreach (string w in other.dictionary) {
				if (!dictionaryMap.ContainsKey(w)) {
					dictionaryMap[w] = combinedDictionary.Count;
					combinedDictionary.Add(w);
				}
			}

			// Remap array that maps other's index to combined index (remap[i] = j where other[i] = combined[j])
			int[] dictionaryOtherRemap = new int[other.dictionary.Length];
			for (int index = 0; index < dictionaryOtherRemap.Length; ++index) {
				string othersCurrentWord = other.dictionary[index];
				dictionaryOtherRemap[index] = dictionaryMap[othersCurrentWord];
			}

			// --- NGram Combining

			// TODO: it's possible to combine ngrams and their links at the same time instead of doing more work

			// Combined ngrams, ngrammap
			List<NGram> combinedNGrams = new List<NGram>(grams) {
				Capacity = grams.Length + other.grams.Length
			};
			Dictionary<NGram, int> ngramMap = new Dictionary<NGram, int>(grams.Length + other.grams.Length);

			// Populate gram map with own grams
			i = 0;
			foreach (NGram gram in grams) {
				ngramMap[gram] = i++;
			}

			// Go through other's ngrams, populate onto combined, and populate ngram remap
			i = 0;
			int[] ngramOtherRemap = new int[other.grams.Length];
			// TODO: consider parallelizing, would involve an add queue and a lock potentially
			foreach (NGram gram in other.grams) {
				// Translate ngram using dictionary remap
				var g = gram.gram.Select((e) => (e == -1) ? -1 : dictionaryOtherRemap[e]);
				NGram remap = new NGram(g);

				if (ngramMap.TryGetValue(remap, out int index)) {
					// If remapped ngram is not unique, remap points to it in combined
					ngramOtherRemap[i++] = index;
				} else {
					// If translated ngram is unique, add it to the end, remap points to it
					ngramOtherRemap[i++] = combinedNGrams.Count;
					combinedNGrams.Add(remap);
				}
			}

			// --- Chain links combining

			//	Other's unique chain links will not need to be touched
			//		Can tell if it's unique by testing whether ngram remap index >= original.length
			//		Remember that ngrams and the links are associated together despite being in seperate arrays (i.e. ngram[0] corresponds with links[0])
			//	For those which need to be comebined, use MarkovSegment combine method

			MarkovSegment[] combinedLinks = new MarkovSegment[combinedNGrams.Count];

			// Populate combined_links with own
			Parallel.For(0, combinedLinks.Length, (index) => {
				combinedLinks[index] = chainLinks[index];
			});

			// Populate linkmap with other
			// TODO: make parallel when done testing
			// Parallel.For(0, other.chain_links.Length, (index) => {
			for (int index = 0; index < other.chainLinks.Length; ++index) {
				var otherSegment = other.chainLinks[index];

				int remap;
				if ((remap = ngramOtherRemap[index]) >= chainLinks.Length) {
					// Unique link needs to be associated with its remap spot
					combinedLinks[remap] = otherSegment;
				} else {
					var ownSegment = chainLinks[remap];
					// Otherwise, combine the segments and replace
					var replace = ownSegment.Combine(otherSegment, ngramOtherRemap, grams.Length);

					// Replace link in relevant structures
					combinedLinks[remap] = replace;
				}
			}
			// });

			// TODO: remove when done testing
			if (combinedLinks.Contains(null)) {
				Console.WriteLine("yeah crazy");
			}

			// --- Seed combining

			//	Run the other's seeds through ngram remap,
			//	Any of other's seeds which are unique (larger than original seed's length), add to end

			List<int> combinedSeeds = new List<int>(seeds) {
				Capacity = seeds.Length + other.seeds.Length
			};

			combinedSeeds.AddRange(from oseed in other.seeds
				where ngramOtherRemap[oseed] >= seeds.Length
				select oseed);

			// Put it all together
			return new MarkovStructure(combinedDictionary.ToArray(),
				combinedNGrams.ToArray(),
				combinedLinks,
				combinedSeeds.ToArray());
		}

		/// <summary>
		/// Constructor for a markovstructure, only to be used by pipeline
		/// </summary>
		/// <param name="dic">Master dictionary, array of words</param>
		/// <param name="grms">Thread-safe master list of ngrams</param>
		/// <param name="prototypeChainlinks">Prototype of chain links, maps ngram-index
		/// to prototype of successors (which maps succeeding index to weight)</param>
		/// <param name="sds">Prototype of seed list, in hash map form for quick access</param>
		public MarkovStructure(string[] dic, ConcurrentQueue<NGram> grms,
			ConcurrentDictionary<int, ConcurrentDictionary<int, int>> prototypeChainlinks,
			ConcurrentDictionary<int, bool> sds) {
			// Pass along master dictionary
			dictionary = dic;

			// Populate master grams table 
			grams = grms.ToArray();

			// Populate chain links
			// Index of any chain link is associated with the ngram of the same index
			chainLinks = new MarkovSegment[grams.Length];
			Parallel.For(0, grams.Length, (ind) => {
				chainLinks[ind] = new MarkovSegment(prototypeChainlinks[ind]);
			});

			// Populate list of seeds
			seeds = sds.Keys.ToArray();
		}

		internal MarkovStructure(string[] dic, NGram[] grms, MarkovSegment[] links, int[] sds) {
			dictionary = dic;
			grams = grms;
			chainLinks = links;
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
		internal NGramSuccessor RandomSuccessor(Random random) {
			return Utils.RandomWeightedChoice(successors, runningTotal, x => x.weight, random);
		}

		// Setup function which will normalize successors and compute running totals
		// TODO: test
		private void SetupSuccessorsAndRunningTotals(IEnumerable<NGramSuccessor> sucs) {
			// Get GDC of weights
			int GDC = Utils.GCD(sucs.Select(e => e.weight));

			// Successors are divided by GDC
			Parallel.ForEach(sucs, (successor) => {
				successor.weight /= GDC;
			});
			successors = sucs.ToArray();

			// Compute running total
			int totalWeight = 0;
			runningTotal = new int[successors.Length];
			for (int ind = 0; ind < runningTotal.Length; ++ind) {
				totalWeight += successors[ind].weight;
				runningTotal[ind] = totalWeight;
			}
		}

		/// <summary>
		/// Constructor of MarkovSegment, meant to be used by MarkovStructure
		/// </summary>
		/// <param name="prototypeSuccessors"></param>
		internal MarkovSegment(ConcurrentDictionary<int, int> prototypeSuccessors) {
			// key is index of current ngram
			// value is map<index of successor, associated weight>

			// Populate successors
			List<NGramSuccessor> successorList = new List<NGramSuccessor>(prototypeSuccessors.Count);

			// Add successors in sorted form
			NGramSuccessor.ReverseComparer reverseComparer = new NGramSuccessor.ReverseComparer();
			foreach (var successor in prototypeSuccessors) successorList.SortAdd(new NGramSuccessor(successor.Key, successor.Value), reverseComparer);

			SetupSuccessorsAndRunningTotals(successorList.ToArray());
		}

		// For constructing from combine function
		internal MarkovSegment(IEnumerable<NGramSuccessor> successors) {
			SetupSuccessorsAndRunningTotals(successors);
		}

		// For constructing from reading from file
		// Prerequesites: successors are sorted from highest weight to lowest weight, running total accurately reflect weight
		internal MarkovSegment(NGramSuccessor[] sucs, int[] runtot) {
			successors = sucs;
			runningTotal = runtot;
		}

		// TODO: add summary, create unit test
		public MarkovSegment Combine(MarkovSegment other,
			int[] ngramOtherRemap,
			int ownNGramLength) {
			if (successors.Length == 0) return other;
			if (other.successors.Length == 0) return this;

			// Combined list, map
			// TODO: consider switching to BST structure (likely SortedSet) to prevent O(n) of list insert???
			List<NGramSuccessor> combinedSuccessors = new List<NGramSuccessor>(successors) {
				Capacity = successors.Length + other.successors.Length
			};
			Dictionary<int, int> successorMap = new Dictionary<int, int>(successors.Length + other.successors.Length);

			// Populate map with own
			int ind = 0;
			foreach (NGramSuccessor successor in successors) {
				// TODO: i dont think it should happen but each entry in here should be unique, maybe some sort of testing whether sucmap already has the index
				successorMap[successor.successorIndex] = ind++;
			}

			var reverseComparer = new NGramSuccessor.ReverseComparer();

			// Combine with other
			foreach (NGramSuccessor otherSuccessor in other.successors) {
				int remap = ngramOtherRemap[otherSuccessor.successorIndex];

				if (remap < ownNGramLength && successorMap.TryGetValue(remap, out int index)) {
					// Given succeeding gram is not unique to other, and within the own, succeeded the current ngram
					// Combine the weights basically

					// TODO: really wanna not have O(n) but idk
					// First, grab the relevant successor and remove
					NGramSuccessor ownSuccessor = combinedSuccessors[index];
					combinedSuccessors.RemoveAt(index);

					// Combine weights
					ownSuccessor.weight += otherSuccessor.weight;

					// And add back (in sorted position)
					combinedSuccessors.SortAdd(ownSuccessor, reverseComparer);
				} else {
					// Either NGram is straight up unique to other, or the ngram is simply not a successor in this particular link
					combinedSuccessors.SortAdd(new NGramSuccessor(remap, otherSuccessor.weight), reverseComparer);
				}
			}

			return new MarkovSegment(combinedSuccessors);
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
		internal readonly int successorIndex;

		/// <summary>
		/// Weight whos magnitude reflects relative frequency of successor
		/// </summary>
		internal int weight;

		internal NGramSuccessor(int suc, int w) {
			successorIndex = suc;
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

		public NGram(IEnumerable<int> g) {
			gram = g.ToArray();
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
				int seed = (int)2509506049;
				int largePrime = (int)4134118063;

				return gram.Aggregate(seed, (hash, field) => (largePrime * hash ^ field.GetHashCode()));
			}
		}

		public override string ToString() {
			return String.Join(" ", gram);
		}
	}

}
