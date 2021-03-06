﻿module FSharpNews.Data.Storage

open System
open System.Configuration
open MongoDB.Bson
open MongoDB.Bson.Serialization.Attributes
open MongoDB.Driver
open MongoDB.Driver.Builders
open FSharpNews.Data
open FSharpNews.Utils

let private log = Logger.create "Storage"
let private minDataDate = DateTime(2014, 3, 1, 0, 0, 0, DateTimeKind.Utc)

type ActivityType =
    | StackExchange = 0
    | Tweet = 1
    | NugetPackage = 2
    | FsSnippet = 3
    | FPish = 4
    | Gist = 5
    | Repository = 6
    | GroupTopic = 7

#if INTERACTIVE
let connectionString = "mongodb://localhost/fsharpnews"
#else
let connectionString = ConfigurationManager.ConnectionStrings.["MongoDB"].ConnectionString
#endif

// todo extract all copy-pasted strings

let private mongoUrl = MongoUrl.Create connectionString
let private client = new MongoClient(mongoUrl)
let private db = client.GetServer().GetDatabase(mongoUrl.DatabaseName)
let private activities = db.GetCollection("activities")

do activities.EnsureIndex(IndexKeys.Ascending("activity.site").Ascending("activity.questionId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.tweetId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.packageId").Ascending("activity.version"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.snippetId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.fpishId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.gistId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.repoId"), IndexOptions.SetUnique(true).SetSparse(true))
do activities.EnsureIndex(IndexKeys.Ascending("activity.topicId"), IndexOptions.SetUnique(true).SetSparse(true))

let private doc (elems: BsonElement list) = BsonDocument(elems)
let private el (name: string) (value: BsonValue) = BsonElement(name, value)
let private i32 value = BsonInt32 value
let private i64 value = BsonInt64 value
let private str value = BsonString value
let private optstr opt =
    match opt with
    | Some s -> (str s) :> BsonValue
    | None -> BsonNull.Value :> BsonValue
let private date (value: DateTime) = BsonDateTime value
let private ar<'a when 'a :> BsonValue> (values: 'a seq) = BsonArray(values)

let private (|BNull|_|) (v: BsonValue) =
   if v.IsBsonNull
   then Some ()
   else None

let private continueOnError = MongoInsertOptions()
continueOnError.Flags <- InsertFlags.ContinueOnError

let private siteToBson = function Stackoverflow -> i32 0 | Programmers -> i32 1 | CodeReview -> i32 2 | CodeGolf -> i32 3
let private bsonToSite = function 0 -> Stackoverflow | 1 -> Programmers | 2 -> CodeReview | 3 -> CodeGolf | x -> failwithf "Unknown %d StackExchange site" x

let private mapToDocument (activity, raw) =
    let activityDoc, descriminator =
        match activity with
        | StackExchangeQuestion q -> doc [ el "questionId" (i32 q.Id)
                                           el "site" (siteToBson q.Site)
                                           el "title" (str q.Title)
                                           el "userDisplayName" (str q.UserDisplayName)
                                           el "url" (str q.Url)
                                           el "date" (date q.CreationDate) ]
                                     , i32 (int ActivityType.StackExchange)
        | Tweet t -> doc [ el "tweetId" (i64 t.Id)
                           el "text" (str t.Text)
                           el "userId" (i64 t.UserId)
                           el "userScreenName" (str t.UserScreenName)
                           el "date" (date t.CreationDate)
                           el "urls" (ar (t.Urls |> List.map (fun u -> doc [ el "url" (str u.Url)
                                                                             el "expandedUrl" (str u.ExpandedUrl)
                                                                             el "displayUrl" (str u.DisplayUrl)
                                                                             el "startIndex" (i32 u.StartIndex)
                                                                             el "endIndex" (i32 u.EndIndex) ])))
                           el "photo" (match t.Photo with
                                       | Some p -> doc [ el "url" (str p.Url)
                                                         el "mediaUrl" (str p.MediaUrl)
                                                         el "displayUrl" (str p.DisplayUrl)
                                                         el "startIndex" (i32 p.StartIndex)
                                                         el "endIndex" (i32 p.EndIndex) ] :> BsonValue
                                       | None -> BsonNull.Value :> BsonValue) ]
                     , i32 (int ActivityType.Tweet)
        | NugetPackage p -> doc [ el "packageId" (str p.Id)
                                  el "version" (str p.Version)
                                  el "url" (str p.Url)
                                  el "date" (date p.PublishedDate) ]
                            , i32 (int ActivityType.NugetPackage)
        | FsSnippet s -> doc [ el "snippetId" (str s.Id)
                               el "title" (str s.Title)
                               el "author" (str s.Author)
                               el "url" (str s.Url)
                               el "date" (date s.PublishedDate) ]
                         , i32 (int ActivityType.FsSnippet)
        | FPishQuestion q -> doc [ el "fpishId" (i32 q.Id)
                                   el "title" (str q.Title)
                                   el "author" (str q.Author)
                                   el "url" (str q.Url)
                                   el "date" (date q.PublishedDate) ]
                             , i32 (int ActivityType.FPish)
        | Gist g -> doc [ el "gistId" (str g.Id)
                          el "description" (optstr g.Description)
                          el "owner" (str g.Owner)
                          el "url" (str g.Url)
                          el "date" (date g.CreationDate) ]
                    , i32 (int ActivityType.Gist)
        | Repository r -> doc [ el "repoId" (i32 r.Id)
                                el "name" (str r.Name)
                                el "description" (optstr r.Description)
                                el "owner" (str r.Owner)
                                el "url" (str r.Url)
                                el "date" (date r.CreationDate) ]
                          , i32 (int ActivityType.Repository)
        | GroupTopic t -> doc [ el "topicId" (str t.Id)
                                el "title" (str t.Title)
                                el "starter" (str t.Starter)
                                el "url" (str t.Url)
                                el "date" (date t.CreationDate) ]
                          , i32 (int ActivityType.GroupTopic)
    doc [ el "descriminator" descriminator
          el "activity" activityDoc
          el "raw" (str raw)
          el "addedDate" (date DateTime.UtcNow) ]

let private mapFromDocument (document: BsonDocument) =
    let activityType = enum<ActivityType>(document.["descriminator"].AsInt32)
    let adoc = document.["activity"].AsBsonDocument
    let activity =
        match activityType with
        | ActivityType.StackExchange -> { Id = adoc.["questionId"].AsInt32
                                          Site = bsonToSite adoc.["site"].AsInt32
                                          Title = adoc.["title"].AsString
                                          UserDisplayName = adoc.["userDisplayName"].AsString
                                          Url = adoc.["url"].AsString
                                          CreationDate = adoc.["date"].ToUniversalTime() } |> StackExchangeQuestion
        | ActivityType.Tweet -> { Id = adoc.["tweetId"].AsInt64
                                  Text = adoc.["text"].AsString
                                  UserId = adoc.["userId"].AsInt64
                                  UserScreenName = adoc.["userScreenName"].AsString
                                  CreationDate = adoc.["date"].ToUniversalTime()
                                  Urls = adoc.["urls"].AsBsonArray.Values
                                         |> Seq.map (fun bv -> bv.AsBsonDocument)
                                         |> Seq.map (fun bd -> { Url = bd.["url"].AsString
                                                                 ExpandedUrl = bd.["expandedUrl"].AsString
                                                                 DisplayUrl = bd.["displayUrl"].AsString
                                                                 StartIndex = bd.["startIndex"].AsInt32
                                                                 EndIndex = bd.["endIndex"].AsInt32 })
                                         |> Seq.toList
                                  Photo = match adoc.["photo"] with
                                          | BNull -> None
                                          | bv -> let photo = bv.AsBsonDocument
                                                  Some { Url = photo.["url"].AsString
                                                         MediaUrl = photo.["mediaUrl"].AsString
                                                         DisplayUrl = photo.["displayUrl"].AsString
                                                         StartIndex = photo.["startIndex"].AsInt32
                                                         EndIndex = photo.["endIndex"].AsInt32 } }
                                |> Tweet
        | ActivityType.NugetPackage -> { Id = adoc.["packageId"].AsString
                                         Version = adoc.["version"].AsString
                                         Url = adoc.["url"].AsString
                                         PublishedDate = adoc.["date"].ToUniversalTime() } |> NugetPackage
        | ActivityType.FsSnippet -> { FsSnippet.Id = adoc.["snippetId"].AsString
                                      Title = adoc.["title"].AsString
                                      Author = adoc.["author"].AsString
                                      Url = adoc.["url"].AsString
                                      PublishedDate = adoc.["date"].ToUniversalTime() } |> FsSnippet
        | ActivityType.FPish -> { FPishQuestion.Id = adoc.["fpishId"].AsInt32
                                  Title = adoc.["title"].AsString
                                  Author = adoc.["author"].AsString
                                  Url = adoc.["url"].AsString
                                  PublishedDate = adoc.["date"].ToUniversalTime() } |> FPishQuestion
        | ActivityType.Gist -> { Gist.Id = adoc.["gistId"].AsString
                                 Description = match adoc.["description"] with
                                               | BNull -> None
                                               | v -> Some v.AsString
                                 Owner = adoc.["owner"].AsString
                                 Url = adoc.["url"].AsString
                                 CreationDate = adoc.["date"].ToUniversalTime() } |> Gist
        | ActivityType.Repository -> { Repository.Id = adoc.["repoId"].AsInt32
                                       Name = adoc.["name"].AsString
                                       Description = match adoc.["description"] with
                                                     | BNull -> None
                                                     | v -> Some v.AsString
                                       Owner = adoc.["owner"].AsString
                                       Url = adoc.["url"].AsString
                                       CreationDate = adoc.["date"].ToUniversalTime() } |> Repository
        | ActivityType.GroupTopic -> { Id = adoc.["topicId"].AsString
                                       Title = adoc.["title"].AsString
                                       Starter = adoc.["starter"].AsString
                                       Url = adoc.["url"].AsString
                                       CreationDate = adoc.["date"].ToUniversalTime() } |> GroupTopic
        | t -> failwithf "Mapping for %A is not implemented" t
    let added = document.["addedDate"].ToUniversalTime()
    activity, added

let private mapToActivities cursor =
    cursor
    |> Seq.cast<BsonDocument>
    |> Seq.map mapFromDocument
    |> Seq.toList

let private safeUniq fn description =
    try
        fn() |> ignore
    with
    | :? WriteConcernException as e ->
        let duplicateKeyError = 11000
        match e.CommandResult.Code with
        | Value code when code = duplicateKeyError -> ()
        | _ -> reraise()

let private safeInsert doc =
    let fn = fun () -> activities.Insert doc
    let desc = fun () -> sprintf "inserting document %O" doc
    safeUniq fn desc

let private safeInsertBatch (docs: BsonDocument list) =
    let fn = fun () -> activities.InsertBatch(docs, continueOnError)
    let desc = fun () -> sprintf "inserting documents %A" (docs |> List.map (sprintf "%O"))
    safeUniq fn desc

let save (activity: Activity, raw: string) =
    (activity, raw)
    |> mapToDocument
    |> safeInsert

let saveAll (activitiesWithRaws: (Activity*string) list) =
    match activitiesWithRaws with
    | [] -> ()
    | activitiesToSave -> activitiesToSave
                          |> List.map mapToDocument
                          |> safeInsertBatch

let getTimeOfLastQuestion (site: StackExchangeSite) =
    activities.Find(Query.And [ Query.EQ("descriminator", i32 (int ActivityType.StackExchange))
                                Query.EQ("activity.site", siteToBson site) ])
              .SetSortOrder(SortBy.Descending("activity.date"))
              .SetLimit 1
    |> Seq.map mapFromDocument
    |> Seq.tryHead
    |> function | Some (StackExchangeQuestion quest, _) -> quest.CreationDate
                | _ -> minDataDate

let getIdOfLastTweet () =
    activities.Find(Query.EQ("descriminator", i32 (int ActivityType.Tweet)))
              .SetSortOrder(SortBy.Descending("activity.tweetId"))
              .SetLimit(1)
    |> Seq.map mapFromDocument
    |> Seq.tryHead
    |> function | Some (Tweet tweet, _) -> tweet.Id
                | _ -> 418099438234894336L - 1L // tweet 1:20 AM - 1 Jan 2014

let getTimeOfLastPackage () =
    activities.Find(Query.EQ("descriminator", i32 (int ActivityType.NugetPackage)))
                         .SetSortOrder(SortBy.Descending("activity.date"))
                         .SetLimit(1)
    |> Seq.map mapFromDocument
    |> Seq.tryHead
    |> function | Some (NugetPackage pkg, _) -> pkg.PublishedDate
                | _ -> minDataDate

let getDateOfLastGist () =
    activities.Find(Query.EQ("descriminator", i32 (int ActivityType.Gist)))
              .SetSortOrder(SortBy.Descending("activity.date"))
              .SetLimit(1)
    |> Seq.map mapFromDocument
    |> Seq.tryHead
    |> function | Some (Gist gist, _) -> gist.CreationDate
                | _ -> minDataDate

let getDateOfLastRepo () =
    activities.Find(Query.EQ("descriminator", i32 (int ActivityType.Repository)))
              .SetSortOrder(SortBy.Descending("activity.date"))
              .SetLimit(1)
    |> Seq.map mapFromDocument
    |> Seq.tryHead
    |> function | Some (Repository repo, _) -> repo.CreationDate
                | _ -> DateTime.MinValue

let getTopActivitiesByCreation count =
    activities
        .FindAll()
        .SetSortOrder(SortBy.Descending("activity.date"))
        .SetLimit(count)
    |> mapToActivities

let getActivitiesAddedSince (dtExclusive: DateTime) =
    activities
        .Find(Query.GT("addedDate", date dtExclusive))
        .SetSortOrder(SortBy.Descending "activity.date")
    |> mapToActivities

let getActivitiesAddedEarlier count (dtExclusive: DateTime) =
    activities.Find(Query.LT("activity.date", BsonDateTime dtExclusive))
                         .SetSortOrder(SortBy.Descending "activity.date")
                         .SetLimit(count)
    |> mapToActivities

let getCountAddedSince (activityType: ActivityType) (dtInclusive: DateTime) =
    activities.Count(Query.And([ Query.EQ("descriminator", i32 (int activityType))
                                 Query.GTE("addedDate", date dtInclusive) ])) |> int

let getCount (activityType: ActivityType) = activities.Count(Query.EQ("descriminator", i32 (int activityType))) |> int

let internal getAllActivities () = activities.FindAll() |> mapToActivities
let internal deleteAll () = do activities.RemoveAll() |> ignore
let internal saveWithAdded (activity, added) =
    let bdoc = mapToDocument (activity, "")
    bdoc.["addedDate"] <- date added
    safeInsert bdoc
