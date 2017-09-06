using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Atlassian.Jira;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Newtonsoft.Json;
using System.Dynamic;

namespace JIRAtoVSTS
{
    class Program
    {
        public static WorkItemTrackingHttpClient witClient;
        public static Jira jiraConn;
        public static bool test = false;
        public static WorkItem addWorkItem(Issue issue, string project, string workItemType, WorkItem parentItem = null )
        {
            Console.WriteLine("-" + issue.Key + " " + issue.Summary);
            JsonPatchDocument issueDocument = new JsonPatchDocument();
            WorkItem issueWorkItem = new WorkItem();
            string issuetitle = String.Format("[{0}] [{1}] {2}", DateTime.Now.ToString("yyyy-MM-dd"), issue.Key, issue.Summary);
            issuetitle = issuetitle.Substring(0, Math.Min(issuetitle.Length, 128));
            issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Title", Value = issuetitle });
            issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Tags", Value = "Jira" });

            if (issue.Description != null)
            {
                issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Description", Value = issue.Description });
            }
            if (issue.Description != null)
            {
                issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/System.Description", Value = issue.Summary });
            }
            //lots of custom fields in jira here is how to identify and convert them
            foreach (CustomFieldValue item in issue.CustomFields)
            {

                if (item.Name == "Acceptance Criteria")
                {
                    issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/Microsoft.VSTS.Common.AcceptanceCriteria", Value = item.Values[0] });
                }
                if (item.Name == "Story Points")
                {
                    issueDocument.Add(new JsonPatchOperation { Operation = Operation.Add, Path = "/fields/Microsoft.VSTS.Scheduling.Effort", Value = int.Parse(item.Values[0]) });
                }
            }
            if (parentItem != null)
            {
                issueDocument.Add(
                    new JsonPatchOperation()
                    {
                        Operation = Operation.Add,
                        Path = "/relations/-",
                        Value = new
                        {
                            rel = "System.LinkTypes.Hierarchy-Reverse",
                            url = parentItem.Url,
                            attributes = new
                            {
                                comment = "Child of " + parentItem.Id.ToString() 
                            }
                        }
                    }
                );
            }
            

            if (test == false)
            {
               return issueWorkItem = witClient.CreateWorkItemAsync(issueDocument, project, workItemType).Result;
            }

            return null;
            
        }

        static void Main(string[] args)
        {
            string project = "myProject";
            //my setup required to look at everything under an initiative
            string parentInitiative = "parentInitiative";
            string vstsUrl = "https://mycompany.visualstudio.com";
            string vstsPAT = "mypersonalaccesstoken";
            VssConnection connection = new VssConnection(new Uri(vstsUrl), new VssBasicCredential(string.Empty, vstsPAT));
            witClient = connection.GetClient<WorkItemTrackingHttpClient>();

            string jUserID = "myjirausername";
            string jPassword = "myjirapassword";
            string jUrl = "https://mycompany.atlassian.net";
            Jira jiraConn = Jira.CreateRestClient(jUrl, jUserID, jPassword);
            jiraConn.MaxIssuesPerRequest = 1000;

            //set a breakpoint here to get a list of all VSTS fields
            //var fieldlist = witClient.GetFieldsAsync("myvstsproject").Result;
            //var testItem = witClient.GetWorkItemAsync(1, expand: WorkItemExpand.All).Result;
            var issues = (from i in jiraConn.Issues.Queryable where i.Created > DateTime.Now.AddDays(-30) orderby i.Priority select i).ToList();

            //inourjira 10000 was the id for an epic and 10001 was story this could potentially be different in yours but unlikely also you may need to add extras for custom types
            IList filterEpics = (from i in issues where i.Type.Id == "10000" && i.CustomFields[2].Values[0] == parentInitiative select i).ToList();
            List<Issue> filterStory = (from i in issues where i.Type.Id == "10001" select i).ToList();
            List<Issue> filterTask = (from i in issues where i.ParentIssueKey != null select i).ToList();

            
            foreach (Issue epic in filterEpics)
            {
                WorkItem epicInfo;
                List<Issue> epicChildStory = new List<Issue>();
                foreach(Issue story in filterStory)
                {
                    foreach( CustomFieldValue item in story.CustomFields)
                    {
                        
                        if (item.Values[0] == epic.Key.Value)
                        {
                            epicChildStory.Add(story);
                        }
                    }
                }
                epicInfo = addWorkItem(epic, project, "Feature");

                foreach (Issue story in epicChildStory)
                {
                    WorkItem storyInfo;
                    storyInfo = addWorkItem(story, project, "Product Backlog Item", epicInfo);
                    List<Issue> StoryChildTask = new List<Issue>();
                    StoryChildTask = (from i in filterTask where i.ParentIssueKey == story.Key.Value select i).ToList();
                    foreach (Issue task in StoryChildTask)
                    {
                        addWorkItem(task, project, "Task", storyInfo);
                    }

                }

            }
            Console.ReadLine();
        }
    }
}
