﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog.Events;
using Serilog.Sinks.Http.BatchFormatters;
using Newtonsoft.Json;
using Serilog.Formatting.Compact;
using Serilog;
using System.Dynamic;
using Newtonsoft.Json.Linq;
using Serilog.Parsing;
using Sejil.Logging.Sinks;
using Sejil.Configuration.Internal;
using Sejil.Logging.Service;

namespace SampleBasic.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LogController : ControllerBase
    {
        [HttpPost(template: "Ajouter")]
        public async Task<IActionResult> PostAjouterAsync()
        {


            string jsonString;
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                jsonString = await reader.ReadToEndAsync();
            }

            var t = JsonConvert.DeserializeObject<Rootobject>(jsonString);

            var sejilService = new SejilService(new SejilSettings("", LogEventLevel.Error));

            await sejilService.EmitBatchAsync(t.events, Request.Query["sourceApp"]);

            return Ok();
        }

        [HttpGet(template: "Test/{id}")]
        public IActionResult GetTest(int id)
        {
            var t = 0;

            return Ok();
        }
    }


    public class Rootobject
    {
        public Event[] events { get; set; }
    }





}