﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Jhu.Graywulf.Web.Services;

namespace Jhu.Graywulf.Web.Api.V1
{
    [TestClass]
    public class JobsServiceTest : ApiTestBase
    {
        protected IJobsService CreateClient(RestClientSession session)
        {
            AuthenticateUser(session);
            var client = session.CreateClient<IJobsService>(new Uri("http://localhost/gwui/api/v1/jobs.svc"));
            return client;
        }


        [TestMethod]
        public void ListQueuesTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);
                var queues = client.ListQueues();
                Assert.AreEqual(2, queues.Queues.Length);
            }
        }

        [TestMethod]
        public void GetQueueTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);
                var queue = client.GetQueue("long");
                queue = client.GetQueue("quick");
            }
        }

        [TestMethod]
        public void ListJobsTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);
                var jobs = client.ListJobs("all", "all", null, null);

                jobs = client.ListJobs("quick", "query", "1", "5");
                jobs = client.ListJobs("long", "export", "1", "5");

                jobs = client.ListJobs("all", "all", "1", "5");
                Assert.AreEqual(5, jobs.Jobs.Length);
            }
        }

        [TestMethod]
        public void GetJobTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);
                // Get some jobs
                var jobs = client.ListJobs("all", "all", null, null);

                // Pick the first one
                var job = client.GetJob(jobs.Jobs[0].GetValue().Guid.ToString());
            }
        }

        [TestMethod]
        public void SubmitQueryJobTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);

                var job = new QueryJob()
                {
                    Query = "SELECT * FROM TEST:SampleData",
                    Comments = "test comments",
                };

                var request = new JobRequest()
                {
                    QueryJob = job
                };

                var response = client.SubmitJob("quick", request);

                // Try to get newly scheduled job
                var nj = client.GetJob(response.QueryJob.Guid.ToString());


                // Now create another job depending on this one

                job = new QueryJob()
                {
                    Query = "SELECT * FROM TEST:SampleData -- JOB 2",
                    Comments = "test comments",
                    Dependencies = new JobDependency[]
                {
                    new JobDependency()
                    {
                        Condition = JobDependencyCondition.Completed,
                        PredecessorJobGuid = nj.QueryJob.Guid
                    }
                }
                };

                request = new JobRequest()
                {
                    QueryJob = job
                };

                response = client.SubmitJob("quick", request);

                var nj2 = client.GetJob(response.QueryJob.Guid.ToString());

                Assert.IsTrue(nj2.QueryJob.Dependencies.Length > 0);
            }
        }

        [TestMethod]
        public void CancelJobTest()
        {
            using (var session = new RestClientSession())
            {
                var client = CreateClient(session);

                // Create a simple job first

                var job = new QueryJob()
                {
                    Query = "SELECT * FROM TEST:SampleData",
                    Comments = "test comments",
                };

                var request = new JobRequest()
                {
                    QueryJob = job
                };

                var response = client.SubmitJob("quick", request);

                // Try to get newly scheduled job
                var nj = client.GetJob(response.QueryJob.Guid.ToString());

                // Now cancel it
                var nj2 = client.CancelJob(response.QueryJob.Guid.ToString());

                Assert.AreEqual(JobStatus.Canceled, nj2.QueryJob.Status);
            }
        }
    }
}