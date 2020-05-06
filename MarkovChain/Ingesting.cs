using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using System.Text.RegularExpressions; // regex
using Microsoft.VisualBasic.FileIO; // For CSV parsing

using MarkovChain.Structs;

/// <summary>
/// Ingesting namespace
/// </summary>
namespace MarkovChain.Ingesting {
	/// <summary>
	/// Struct representing options for ingesting process
	/// </summary>
	public class IngestOptions {
		/// <summary>
		/// Filename for input CSV file
		/// </summary>
		public string infileCSV;

		/// <summary>
		/// Array of string pairs in the form of (reg, rep) where
		/// for each line ingested, the regex is matched by reg and
		/// replaced with rep in the order listed by the array.
		/// </summary>
		public Tuple<string, string>[] regexFilters;

		/// <summary>
		/// Unsigned long representing size of n-gram for a markov chain segment
		/// </summary>
		public int gramSize;
	}

	// Plan: Allow for ingesting to synthesize multiple markovstructures from single input file
	//	Split pipeline into 2 parts
	//		[Ingesting single filter] --(Multiplexed by user-name)--> [The rest] --> Different MarkovStructures

	//	Let's loft every stage into its own class
	//		[MASTER PIPELINE] --
	//		Input CSV --> [INGESTER] --> (Username, Message) -- Message data
	//		Message data --> [MASTER PIPELINE'S RESPONSIBILITY] --> Message string
	//		[POST MUX PIPE COLLECION] --
	//		Message string --> [FILTER] --> Message string
	//		Message string --> [DICTIONARIZER] --> Sentence bank
	//		Sentnece bank --> [MARKOVIZER] --> Markov Structure Prototype

	class SentenceBank {
		public List<string> dictionary;
		public ConcurrentQueue<int[]> sentences;

		public SentenceBank() {
			dictionary = new List<string>();
			sentences = new ConcurrentQueue<int[]>();
		}
	}

	struct MarkovStructProto {
		public IEnumerable<string> dictionary;
		public IEnumerable<NGram> ngrams;
		public ConcurrentDictionary<int, ConcurrentDictionary<int, int>> prototypeChainlinks;
		public ConcurrentDictionary<int, bool> seeds;
	}

	// Ingester class will pull from CSV file
	class Ingester {
		private readonly ConcurrentQueue<Tuple<string, string>> outMessageDatas;
		public bool PipeSealed { get; private set; }

		private readonly string infileCSV;

		public Ingester(string inCSV, ConcurrentQueue<Tuple<string, string>> msgs) {
			infileCSV = inCSV;
			outMessageDatas = msgs;
			PipeSealed = false;
		}
	}

	class Filter {
		public readonly ConcurrentQueue<Tuple<string, string>> inMessageDatas;
		private readonly ConcurrentQueue<string> outMessageStrings;

		private readonly Tuple<string, string>[] regexFilters;

		public Filter(ConcurrentQueue<string> message_strings, Tuple<string, string>[] filters) {
			outMessageStrings = message_strings;
			regexFilters = filters;

			inMessageDatas = new ConcurrentQueue<Tuple<string, string>>();
		}
	}

	class Dictionarizer {
		public readonly ConcurrentQueue<string> inMessageStrings;
		private readonly SentenceBank outSentenceBank;

		public Dictionarizer(SentenceBank bank) {
			outSentenceBank = bank;

			inMessageStrings = new ConcurrentQueue<string>();
		}
	}

	/// <summary>
	/// Main object to facilitate concurrent pipelined ingesting
	/// </summary>
	public abstract class Pipeline {
		// ---Pipeline
		//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
		//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

		// Metaproces related fields
		public readonly IngestOptions options;
		public Status status;

		// Resultant values to grab
		public string[] masterDictionary;
		public MarkovStructure finishedMarkovStructure;

		// Concurrent queues for pipeline
		// set to private later
		public readonly ConcurrentQueue<string> conqueCSV,
												conqueFilter;
		public readonly ConcurrentQueue<int[]> conqueDictionarized;

		// Flags
		private bool flagCSV,
						flagFiltered,
						flagDictionarized;


		// Dictionarizing thread related constructs
		private readonly ConcurrentQueue<string> workingMasterDictionary;
		private readonly ConcurrentDictionary<string, int> workingMasterWordCloud;

		// Markovizing thread related constructs
		private readonly ConcurrentDictionary<NGram, int> workingMasterNGramCloud;

		private readonly ConcurrentQueue<NGram> workingMasterNGrams;
		private readonly ConcurrentDictionary<int, bool> workingMasterSeeds;
		private readonly ConcurrentDictionary<int,
						ConcurrentDictionary<int, int>> workingMasterSuccessors;


		// Enums

		// TODO: change to exceptions or something
		/// <summary>
		/// Various states for ingesting, which 
		/// will sift up by end of the function
		/// </summary>
		public enum Status {
			AllGood,
			ErrorColumnNotFound,
			InvalidGramSize
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="opt"></param>
		public Pipeline(IngestOptions opt) {
			options = opt;

			status = Status.AllGood;

			// Threads
			conqueCSV = new ConcurrentQueue<string>();
			flagCSV = false;

			conqueFilter = new ConcurrentQueue<string>();
			flagFiltered = false;

			conqueDictionarized = new ConcurrentQueue<int[]>();
			flagDictionarized = false;

			// Dictionarizing
			workingMasterDictionary = new ConcurrentQueue<string>();
			workingMasterWordCloud = new ConcurrentDictionary<string, int>();

			// Markovization
			workingMasterNGramCloud = new ConcurrentDictionary<NGram, int>();
			workingMasterNGrams = new ConcurrentQueue<NGram>();
			workingMasterSeeds = new ConcurrentDictionary<int, bool>();
			workingMasterSuccessors = new ConcurrentDictionary<int, ConcurrentDictionary<int, int>>();
		}

		/// <summary>
		/// Master start function
		/// </summary>
		public bool Run() {
			// ---Pipeline
			//	INPUT CSV --(INGESTING) --> RAW STRINGS --(FILTERING)--> LIST OF SENTENCE STRINGS --(DICTIONARIZING)-->
			//	--> SENTENCE BANK --(MARKOVIZING)--> MARKOV STRUCTURE

			// ---Structures
			//	SENTENCE BANK -- DICTIOANRY, SENTENCES
			//	MARKOV STRUCTURE -- DICTIONARY, MARKOV SEGMENTS
			//	MARKOV SEGMENT -- N-GRAM, N-GRAM SUCCESSORS
			//	N-GRAM SUCCESSOR -- N-GRAM, ASSOCIATED WEIGHT

			// TODO: fancy output where each thread uses a callback function to write to specific console line
			//			-output in console size of all conqueues so for large data sets it can be seen progressing

			// Gram size precondition
			if (options.gramSize < 1) {
				status = Status.InvalidGramSize;
				return false;
			}

			Thread threadCSV = new Thread(ThreadCSVIngest);
			Thread threadFilter = new Thread(ThreadFilterLead);
			Thread threadDictionarize = new Thread(ThreadDictionarizeLead);
			Thread threadMarkovize = new Thread(ThreadMarkovizeLead);

			threadCSV.Start();
			threadFilter.Start();
			threadDictionarize.Start();
			threadMarkovize.Start();

			threadCSV.Join();
			threadFilter.Join();
			threadDictionarize.Join();
			threadMarkovize.Join();

			return status == Status.AllGood;
		}

		// Threads

		/// <summary>
		/// CSV Ingesting stage master thread
		/// </summary>
		private void ThreadCSVIngest() {
			Console.WriteLine("[CSV]: Starting...");
			using (TextFieldParser parser = new TextFieldParser(options.infileCSV)) {
				parser.TextFieldType = FieldType.Delimited;
				parser.SetDelimiters(";");

				// Read first row
				string[] fields;
				fields = parser.ReadFields();

				// Discover index for relevant column (options.csv_column)
				uint columnIndex = 0;
				foreach (string f in fields) {
					if (f == "Content") break;
					++columnIndex;
				}

				// If no index discovered, failure
				if (columnIndex == fields.Length) {
					status = Status.ErrorColumnNotFound;
					return;
				}

				// While not end of stream, read off specific column, push onto filter queue
				while (!parser.EndOfData) {
					fields = parser.ReadFields();
					string msg = fields[columnIndex];

					if (msg != "") conqueCSV.Enqueue(msg);
				}
			}

			Console.WriteLine("[CSV]: Finished!");
			flagCSV = true;
		}

		/// <summary>
		/// Filtering leader thread. Launches filtering work
		/// thread(s), manages finished flag for filtering
		/// </summary>
		/// <remarks>Finished flag :- all filtering thread(s) are finished</remarks>
		private void ThreadFilterLead() {
			//	Filtering master thread --
			//		Launches all filtering threads
			//		Manages finished flag for filtering
			//		Finished flag :- all filtering threads are finished

			int concur = 1; // TODO: put this into options

			Task[] workers = new Task[concur];

			Console.WriteLine("[Filter Lead]: Dispatching {0} workers...", concur);

			for (int i = 0; i < concur; ++i) {
				workers[i] = Task.Run(() => ThreadFilterWork(i));
			}

			Task.WaitAll(workers);

			Console.WriteLine("[Filter Lead]: Workers finished!");

			flagFiltered = true;
		}

		/// <summary>
		/// Filtering work thread(s).
		/// </summary>
		private void ThreadFilterWork(int id) {
			//	Filtering thread(s) --
			//		until CSV Ingest is finished (known by flag),
			//		take one line and run through filters, then queue onto sentence string queue for dictionarizing
			//		Finished flag :- CSV Ingest is finished, Filtering queue is empty

			// TODO: consider switching to local queues which leading thread populates some day (may improve concurrency?)

			Console.WriteLine("[Filter #{0}]: Starting...", id);

			// Stop :- csv_ingest_finihed, conqueue_csv.IsEmpty
			while (!(flagCSV && conqueCSV.IsEmpty)) {
				if (conqueCSV.TryDequeue(out string piece)) {
					// Take piece out, run through filters, enqueue if applicable
					foreach (var filter in options.regexFilters) {
						piece = Regex.Replace(piece, filter.Item1, filter.Item2);

						// No bother filtering if string is already empty
						if (piece == "") break;
					}

					// Skip enqueuing string is empty
					if (piece == "") continue;

					conqueFilter.Enqueue(piece);
				} else {
					// me guess is csv finished flag is still false, queue is empty waiting to be filled
					// very slim chance flag is true, and there was a small race condition between
					// entering the while and pulling
					Thread.Yield();
				}
			}

			Console.WriteLine("[Filter #{0}]: Finished!", id);
		}

		private void ThreadDictionarizeLead() {
			//	Dictionarization master thread --
			//		Has master word-cloud, word-list, sentence queue for markovization
			//		Finished flag :-	all dictionarization threads are themselves finished,
			//							their local lists have been processed into master dictioanry,
			//							master dictionary has been written out,
			//							all sentences from each local thread have been enqueued--
			//								--into master sentence queue for markovization

			int concur = 1; // TODO: put this into options someday

			// Master word cloud, master word list

			// Launch threads
			Task[] workers = new Task[concur];

			Console.WriteLine("[Dictionarize Lead]: Dispatching {0} workers...", concur);

			for (int i = 0; i < concur; ++i) {
				workers[i] = Task.Run(() => ThreadDictionarizeWork(i));
			}

			Task.WaitAll(workers);

			Console.WriteLine("[Dictionarize Lead]: Workers finished!");

			// Transform working master dictionary to final master dictionary
			masterDictionary = workingMasterDictionary.ToArray();

			flagDictionarized = true;
		}

		private void ThreadDictionarizeWork(int id) {
			//	Dictionarizing thread(s) --
			//		Dequeue a sentence string from preceeding filtered queue
			//		construct dictionarize w/ master cloud and master list
			//		push dictionarized sentence to conqueue
			//		Finished flag :- Filtering is finished, filtered strings queue is empty

			Console.WriteLine("[Dictionarize #{0}]: Starting...", id);

			while (!flagFiltered || !conqueFilter.IsEmpty) {
				List<int> cursent; // current dictionarized sentence

				if (conqueFilter.TryDequeue(out string sentence)) {
					// We have a sentence, dictionarize each word
					cursent = new List<int>();

					// Dictionarize
					foreach (string word in sentence.WordList()) {
						if (!workingMasterWordCloud.TryGetValue(word, out int index)) {
							// There is no index for the current word, we must add one
							index = workingMasterDictionary.Count();

							workingMasterDictionary.Enqueue(word);
							workingMasterWordCloud[word] = index;
						} // else, the trygetvalue succeeded, we have an index (no further action necessary)

						cursent.Add(index);
					}

					// Add "end of sentence" symbol
					cursent.Add(-1);

					// Enqueue array onto conqueue
					conqueDictionarized.Enqueue(cursent.ToArray());
				} else {
					// waiting on queue to be filled
					Thread.Yield();
				}
			}

			Console.WriteLine("[Dictionarize #{0}]: Finished!", id);
		}

		private void ThreadMarkovizeLead() {
			//	Markovizing master thread -- 
			//		Has master ngrams collection, concurrentqueue of ngrams which will be referred by indeces in other vars
			//		Has master ngram seed collection, concurrent bag of integers which point to indeces
			//		Has successor dictionary which maps an index to a counting dictionary -- prototype to markovstructure struct
			//			Counter dictionary maps index to count -- prototype to ngram-successor struct
			//		Launches all markovizing threads
			//		Whenever all markovization threads are finished-- construct MarkovStructure with structures

			int concur = 1; // TOOD: put this into options some day

			// Launch threads
			Task[] workers = new Task[concur];

			Console.WriteLine("[Markovization Lead]: Dispatching {0} workers...", concur);

			for (int i = 0; i < concur; ++i) {
				workers[i] = Task.Run(() => ThreadMarkovizeWork(i));
			}

			Task.WaitAll(workers);

			Console.WriteLine("[Markovization Lead]: Workers finished!");

			// Create finished markovization product
			finishedMarkovStructure = new MarkovStructure(masterDictionary, workingMasterNGrams,
										workingMasterSuccessors, workingMasterSeeds);
		}

		private void ThreadMarkovizeWork(int id) {
			//	Markovizing thread(s) --
			//		Takes a dictionarized sentence off queue if available
			//		Markovizing sentence --
			//			Starting index at 1
			//			Declare current gram, new gram
			//			Grab first gram:
			//			If sentence size is lte gram size
			//				current gram size is sentence size, grab available words, process into current gram
			//			Otherwsie grab gram size, set as current gram
			//			Put this first gram in seed list, if not there already
			//			Loop until positioned where gram size grabs last word (pos < len-size) --
			//				Grab new gram of gram size in overlapping fashion (from index to index+gram size)
			//				In current grams successor's, incremeent count pointed to by new gram
			//				If no count exists (new successor), set count pointed to by new gram to 1
			//				Set current gram = new gram
			//				Increment index
			//		Finished flag :- dictionarized conqueue is empty, dictionarization flag is true

			while (!conqueDictionarized.IsEmpty || !flagDictionarized) {
				int pos, // position along sentence
					index, // index of current ngram
					indexOfSuccessor; // index of succeeding ngram

				NGram curgram;

				if (conqueDictionarized.TryDequeue(out int[] cursent)) {
					pos = 0;

					// Grab firs gram

					// Short-circuit procedure for when sentence is to be made up of one gram
					if (cursent.Length <= options.gramSize) {
						// Sentence is one gram which may or may not be short
						curgram = new NGram(cursent);

						// Register, set as seed
						workingMasterSeeds[MarkovizationRegisterNGram(ref curgram)] = true;

						// Move on with next sentence
						continue;
					}

					//Regular procedure, grabs first gram, regisers, and loops to the end grabbing grams
					curgram = MarkovizationIngestNGram(cursent, pos++);
					index = MarkovizationRegisterNGram(ref curgram);

					// Register as seed
					workingMasterSeeds[index] = true;

					//	In cases where sentence is made of more than 1 gram
					//		visualization, length is 6, gram-size is 3:
					//			0 1 2 3 4 -1
					//			      ^stop
					//		pos <= 6[length] - 3[size]
					while (pos <= cursent.Length - options.gramSize) {
						curgram = MarkovizationIngestNGram(cursent, pos++);
						indexOfSuccessor = MarkovizationRegisterNGram(ref curgram);

						// Update (or establish) successor counter
						if (workingMasterSuccessors[index].ContainsKey(indexOfSuccessor)) {
							workingMasterSuccessors[index][indexOfSuccessor] = workingMasterSuccessors[index][indexOfSuccessor] + 1;
						} else {
							workingMasterSuccessors[index][indexOfSuccessor] = 1;
						}

						// Shift out the old
						index = indexOfSuccessor;
					}
				} else {
					Thread.Yield();
				}
			}
		}

		/// <summary>
		/// Ingests NGram
		/// </summary>
		/// <param name="cursent"></param>
		/// <param name="position"></param>
		/// <returns></returns>
		private NGram MarkovizationIngestNGram(int[] cursent, int position) {
			int[] protoGram = new int[options.gramSize];
			Array.Copy(cursent, position, protoGram, 0, options.gramSize);
			return new NGram(protoGram);
		}

		/// <summary>
		/// Add NGram to master structure, return its index
		/// </summary>
		/// <param name="gram"></param>
		/// <returns></returns>
		private int MarkovizationRegisterNGram(ref NGram gram) {
			// Get corresponding index of first
			if (!workingMasterNGramCloud.TryGetValue(gram, out int index)) {
				// Gram is unique as of yet
				index = workingMasterNGrams.Count();

				// Put in list, get index
				workingMasterNGrams.Enqueue(gram);
				workingMasterNGramCloud[gram] = index;

				// Create new successors dictioanry
				workingMasterSuccessors[index] = new ConcurrentDictionary<int, int>();
			}

			return index;
		}
	}
}
