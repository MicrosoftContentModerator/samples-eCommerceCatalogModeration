using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;


namespace Fusion
{
    class Program
    {
        public enum CallType { POST, GET };

        // Maximum number of tags that the Content Moderator review tool can show
        public const int MAXTAGSCOUNT = 7;

        public const string ContentModeratorKey = "c7189494086d43d9895f5dae73d261de";
        public const string ComputerVisionKey = "0f0669d909ad4b12b2f2260e5e5dd941";
        public const string CustomVisionKey = "8e0260513fb84a7697e85732ff24c912";

        // All your end points based on the new account and subscriptions
        public const string ImageUri = "https://westus.api.cognitive.microsoft.com/contentmoderator/moderate/v1.0/ProcessImage/Evaluate";
        public const string ReviewUri = "https://westus.api.cognitive.microsoft.com/contentmoderator/review/v1.0/teams/m2september/reviews";
        public const string ComputerVisionUri = "https://westcentralus.api.cognitive.microsoft.com/vision/v1.0/analyze?details=celebrities";
        public const string CustomVisionUri = "https://southcentralus.api.cognitive.microsoft.com/customvision/v1.0/Prediction/e594d6b0-b42a-408f-9795-765708819f4a/url";
   
        public static int _ReviewIndexNext = 2;

        static void Main(string[] args)
        {
            // This is where we will save the review tags to be created within the Content Moderator reveiw tool.
            KeyValuePair[] ReviewTags;

            // Check for a test directory for a text file with the list of Image URLs to scan
            var topdir = @"C:\test\";
            var Urlsfile = topdir + "Urls.txt";

            if (!Directory.Exists(topdir))
                return;

            if (!File.Exists(Urlsfile))
            {
                return;
            }

            // Read all image URLs in the file
            var Urls = File.ReadLines(Urlsfile);

            // for each image URL in the file...
            foreach (var Url in Urls)
            {
                // Initiatize a new review tags array
                ReviewTags = new KeyValuePair[MAXTAGSCOUNT];

                // Evaluate for potential adult and racy content with Content Moderator API
                EvaluateAdultRacy(Url, ref ReviewTags);

                // Evaluate for potential presence of celebrity (ies) in images with Computer Vision API
                EvaluateComputerVisionTags(Url, ComputerVisionUri, ComputerVisionKey, ref ReviewTags);

                // Evaluate for potential presence of custom categories other than Marijuana
                EvaluateCustomVisionTags(Url, CustomVisionUri, CustomVisionKey, ref ReviewTags);

                // Create review in the Content Moderator review tool
                CreateReview(Url, ReviewTags);
            }
        }

        /// <summary>
        /// Use Content Moderator API to evaluate for potential adult and racy content
        /// </summary>
        /// <param name="ImageUrl"></param>
        /// <param name="ReviewTags"></param>
        /// <returns>API call success or not</returns>
        public static bool EvaluateAdultRacy(string ImageUrl, ref KeyValuePair[] ReviewTags)
        {
            float AdultScore = 0;
            float RacyScore = 0;

            var File = ImageUrl;
            string Body = $"{{\"DataRepresentation\":\"URL\",\"Value\":\"{File}\"}}";

            HttpResponseMessage response = CallAPI(ImageUri, ContentModeratorKey, CallType.POST,
                                                   "Ocp-Apim-Subscription-Key", "application/json", "", Body);

            if (response.IsSuccessStatusCode)
            {
                // {“answers”:[{“answer”:“Hello”,“questions”:[“Hi”],“score”:100.0}]}
                // Parse the response body. Blocking!
                GetAdultRacyScores(response.Content.ReadAsStringAsync().Result, out AdultScore, out RacyScore);
            }

            ReviewTags[0] = new KeyValuePair();
            ReviewTags[0].Key = "a";
            ReviewTags[0].Value = "false";
            if (AdultScore > 0.4)
            {
                ReviewTags[0].Value = "true";
            }

            ReviewTags[1] = new KeyValuePair();
            ReviewTags[1].Key = "r";
            ReviewTags[1].Value = "false";
            if (RacyScore > 0.3)
            {
                ReviewTags[1].Value = "true";
            }
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Use Computer Vision API to evaluate for potential celebrity presence in image
        /// </summary>
        /// <param name="ImageUrl"></param>
        /// <param name="ComputerVisionUri"></param>
        /// <param name="ComputerVisionKey"></param>
        /// <param name="ReviewTags"></param>
        /// <returns>API call success or not</returns>
        public static bool EvaluateComputerVisionTags(string ImageUrl, string ComputerVisionUri, string ComputerVisionKey, ref KeyValuePair[] ReviewTags)
        {
            var File = ImageUrl;
            string Body = $"{{\"URL\":\"{File}\"}}";

            HttpResponseMessage Response = CallAPI(ComputerVisionUri, ComputerVisionKey, CallType.POST,
                                                   "Ocp-Apim-Subscription-Key", "application/json", "", Body);

            if (Response.IsSuccessStatusCode)
            {
                ReviewTags[2] = new KeyValuePair();
                ReviewTags[2].Key = "cb";
                ReviewTags[2].Value = "false";

                ComputerVisionPrediction CVObject = JsonConvert.DeserializeObject<ComputerVisionPrediction>(Response.Content.ReadAsStringAsync().Result);

                if ((CVObject.categories[0].detail != null) && (CVObject.categories[0].detail.celebrities.Count() > 0))
                {                 
                    ReviewTags[2].Value = "true";
                }
            }

            return Response.IsSuccessStatusCode;
        }

       /// <summary>
       /// Use Custom Vision API to evaluate for potential content from custom-trained categories 
       /// </summary>
       /// <param name="ImageUrl"></param>
       /// <param name="CustomVisionUri"></param>
       /// <param name="CustomVisionKey"></param>
       /// <param name="ReviewTags"></param>
       /// <returns>API call success or not</returns>
        public static bool EvaluateCustomVisionTags(string ImageUrl, string CustomVisionUri, string CustomVisionKey, ref KeyValuePair[] ReviewTags)
        {
            var File = ImageUrl;
            string Body = $"{{\"URL\":\"{File}\"}}";

            HttpResponseMessage response = CallAPI(CustomVisionUri, CustomVisionKey, CallType.POST,
                                                   "Prediction-Key", "application/json", "", Body);

            if (response.IsSuccessStatusCode)
            {
                // Parse the response body. Blocking!
                SaveCustomVisionTags(response.Content.ReadAsStringAsync().Result, ref ReviewTags);
            }

            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Call Content Moderator's Review API to create a review with the image (URL) and the review tags as inputs
        /// </summary>
        /// <param name="ImageUrl"></param>
        /// <param name="Metadata"></param>
        /// <returns>API call success or not</returns>
        public static bool CreateReview(string ImageUrl, KeyValuePair[] Metadata)
        {

            ReviewCreationRequest Review = new ReviewCreationRequest();
            Review.Item[0] = new ReviewItem();
            Review.Item[0].Content = ImageUrl;
            Review.Item[0].Metadata = new KeyValuePair[MAXTAGSCOUNT];
            Metadata.CopyTo(Review.Item[0].Metadata, 0);

            //SortReviewItems(ref Review);

            string Body = JsonConvert.SerializeObject(Review.Item);

            HttpResponseMessage response = CallAPI(ReviewUri, ContentModeratorKey, CallType.POST,
                                                   "Ocp-Apim-Subscription-Key", "application/json", "", Body);

            return response.IsSuccessStatusCode;
        }


        /// <summary>
        /// Extracts the adult and racy probability scores from the Content Moderator API call response JSON output
        /// </summary>
        /// {"AdultClassificationScore":0.044444210827350616,"IsImageAdultClassified":false,"RacyClassificationScore":0.096262000501155853,"IsImageRacyClassified":false,"AdvancedInfo":[],"Result":false,"Status":{"Code":3000,"Description":"OK","Exception":null},"TrackingId":"WU_m2tac.................."}
        /// <param name="Response"></param>
        /// <param name="AdultScore"></param>
        /// <param name="RacyScore"></param>
        static void GetAdultRacyScores(string Response, out float AdultScore, out float RacyScore)
        {
            AdultScore = RacyScore = 0;

            ContentModeratorPrediction CMPrediction = JsonConvert.DeserializeObject<ContentModeratorPrediction>(Response);

            AdultScore = CMPrediction.AdultClassificationScore;
            RacyScore = CMPrediction.RacyClassificationScore;
        }


        /// <summary>
        /// From the Custom Vision response, get the tags and return their short codes and boolean values (true/false)
        /// </summary>
        /// <param name="Response"></param>
        /// <param name="ReviewTags"></param>
        static void SaveCustomVisionTags(string Response, ref KeyValuePair[] ReviewTags)
        {

            CustomVisionPrediction CustomVision = JsonConvert.DeserializeObject<CustomVisionPrediction>(Response);

            int Count = CustomVision.Predictions.Count();

            if (ReviewTags[3] == null)
            {
                ReviewTags[3] = new KeyValuePair();
                ReviewTags[3].Key = GetShortCode(CustomVision.Predictions[0].Tag);
                ReviewTags[3].Value = GetTrueFalse(CustomVision.Predictions[0].Tag, CustomVision.Predictions[0].Probability);
            }

            if (ReviewTags[4] == null)
            {
                ReviewTags[4] = new KeyValuePair();
                ReviewTags[4].Key = GetShortCode(CustomVision.Predictions[1].Tag);
                ReviewTags[4].Value = GetTrueFalse(CustomVision.Predictions[1].Tag, CustomVision.Predictions[1].Probability);
            }

            if (ReviewTags[5] == null)
            {
                ReviewTags[5] = new KeyValuePair();
                ReviewTags[5].Key = GetShortCode(CustomVision.Predictions[2].Tag);
                ReviewTags[5].Value = GetTrueFalse(CustomVision.Predictions[2].Tag, CustomVision.Predictions[2].Probability);
            }

            if (ReviewTags[6] == null)
            {
                ReviewTags[6] = new KeyValuePair();
                ReviewTags[6].Key = GetShortCode(CustomVision.Predictions[3].Tag);
                ReviewTags[6].Value = GetTrueFalse(CustomVision.Predictions[3].Tag, CustomVision.Predictions[3].Probability);
            }


        }

        /// <summary>
        /// Convert labels from the Custom Vision response, to the short codes defined in the Content Moderator review tool
        /// </summary>
        /// <param name="Label"></param>
        /// <returns>the short code</returns>
        static string GetShortCode(string Label)
        {
            string ShortCode = string.Empty;

            switch (Label.ToLower())
            {
                case "flags": ShortCode = "fl"; break;
                case "usa": ShortCode = "us"; break;
                case "toy": ShortCode = "to"; break;
                case "pens": ShortCode = "pn"; break;
                default: ShortCode = Label; ; break;
            }

            return ShortCode;
        }

        /// <summary>
        /// Set the true/false (highlight or not) values for each label or short code. 
        /// If label does not have a matching short code, return the label as is.
        /// </summary>
        /// <param name="Label"></param>
        /// <param name="Probability"></param>
        /// <returns>"true" or "false" to highlight (or not) the short code in the review tool</returns>
        static string GetTrueFalse(string Label, float Probability)
        {
            string Value = "false";

            //- we will use global threshold for now
            if (Probability > 0.5)
            {
                Value = "true";
            }

            return Value;
        }

        /// <summary>
        /// The HTTP API call method that's used to call all REST APIs.
        /// </summary>
        /// <param name="Uri"></param>
        /// <param name="Key"></param>
        /// <param name="Type"></param>
        /// <param name="AuthenticationLabel"></param>
        /// <param name="ContentType"></param>
        /// <param name="UrlParameter"></param>
        /// <param name="Body"></param>
        /// <returns></returns>
        public static HttpResponseMessage CallAPI(string Uri, string Key, CallType Type,
                                                    string AuthenticationLabel, string ContentType,
                                                    string UrlParameter, string Body)
        {

            if (!String.IsNullOrEmpty(UrlParameter))
            {
                Uri += "?" + UrlParameter;
            }

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(Uri);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(ContentType));

            client.DefaultRequestHeaders.Add(AuthenticationLabel, Key);

            HttpResponseMessage response = null;

            if (Type == CallType.POST)
            {
                response = client.PostAsync(Uri, new StringContent(
                                   Body, System.Text.Encoding.UTF8, ContentType)).Result;
            }
            else if (Type == CallType.GET)
            {
                response = client.GetAsync(Uri).Result;
            }

            return response;
        }
    }

    /// <summary>
    /// Content Moderator Response Object
    /// </summary>
    public class ContentModeratorPrediction
    {
        public float AdultClassificationScore { get; set; }
        public bool IsImageAdultClassified { get; set; }
        public float RacyClassificationScore { get; set; }
        public bool IsImageRacyClassified { get; set; }
        public object[] AdvancedInfo { get; set; }
        public bool Result { get; set; }
        public Status Status { get; set; }
        public string TrackingId { get; set; }
    }

    public class Status
    {
        public int Code { get; set; }
        public string Description { get; set; }
        public object Exception { get; set; }
    }

    
    /// <summary>
    /// Content Moderator Review Creation Request Object
    /// </summary>
    public class ReviewCreationRequest
    {
        public ReviewItem[] Item { get; set; }

        public ReviewCreationRequest()
        {
            Item = new ReviewItem[1];
        }
    }

    public class ReviewItem
    {
        public string Content { get; set; }
        public string ContentId { get; set; }
        public KeyValuePair[] Metadata { get; set; }
        public string Type { get; set; }
        public string CallbackEndpoint { get; set; }

        public ReviewItem()
        {
            ContentId = "1";
            Type = "Image";
            CallbackEndpoint = "";
        }
    }

    public class KeyValuePair
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// Custom Vision Response Object
    /// </summary>
    public class CustomVisionPrediction
    {
        public string Id { get; set; }
        public string Project { get; set; }
        public string Iteration { get; set; }
        public DateTime Created { get; set; }
        public Prediction[] Predictions { get; set; }
    }

    public class Prediction
    {
        public string TagId { get; set; }
        public string Tag { get; set; }
        public float Probability { get; set; }
    }

    /// <summary>
    /// Computer Vision Response Object
    /// </summary>
    public class ComputerVisionPrediction
    {
        public Category[] categories { get; set; }
        public string requestId { get; set; }
        public Metadata metadata { get; set; }
    }

    public class Metadata
    {
        public int width { get; set; }
        public int height { get; set; }
        public string format { get; set; }
    }

    public class Category
    {
        public string name { get; set; }
        public float score { get; set; }
        public Detail detail { get; set; }
    }

    public class Detail
    {
        public Celebrity[] celebrities { get; set; }
    }

    public class Celebrity
    {
        public string name { get; set; }
        public Facerectangle faceRectangle { get; set; }
        public float confidence { get; set; }
    }

    public class Facerectangle
    {
        public int left { get; set; }
        public int top { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }



}