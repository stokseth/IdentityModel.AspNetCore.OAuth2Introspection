﻿// Copyright (c) Dominick Baier & Brock Allen. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using FluentAssertions;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Builder;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Tests.Util;
using Xunit;

namespace Tests
{
    public class Introspection
    {
        Action<OAuth2IntrospectionOptions> _options = (o) =>
        {
            o.Authority = "https://authority.com";
            o.DiscoveryPolicy.RequireKeySet = false;

            o.ClientId = "scope";
            o.ClientSecret = "secret";
        };

        [Fact]
        public async Task Unauthorized_Client()
        {
            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Unauthorized);
            });

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ActiveToken()
        {
            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);
            });

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ActiveToken_With_Caching_Ttl_Longer_Than_Duration()
        {
            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);

                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);

            }, true);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ActiveToken_With_Caching_Ttl_Shorter_Than_Duration()
        {
            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);

                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromMinutes(5));
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, true);

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);

            result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task InactiveToken()
        {
            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Inactive);

            });

            client.SetBearerToken("sometoken");

            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ActiveToken_With_SavedToken()
        {
            var expectedToken = "expected_token";

            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);
                o.SaveToken = true;
            });

            client.SetBearerToken(expectedToken);

            var response = await client.GetAsync("http://test");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var responseDataStr = await response.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);

            responseData.Should().Contain("token", expectedToken);
        }

        [Fact]
        public async Task ActiveToken_With_SavedToken_And_Caching()
        {
            var expectedToken = "expected_token";

            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));
                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, true);

            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var responseDataStr = await secondResponse.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);

            responseData.Should().Contain("token", expectedToken);
        }

        [Fact]
        public async Task Repeated_active_token_with_caching_enabled_should_hit_cache()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active, TimeSpan.FromHours(1));

            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = handler;
                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, true);

            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");

            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            handler.SentIntrospectionRequest.Should().BeTrue();

            var responseDataStr = await firstResponse.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);
            responseData.Should().Contain("token", expectedToken);

            handler.SentIntrospectionRequest = false;
            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            handler.SentIntrospectionRequest.Should().BeFalse();

            responseDataStr = await secondResponse.Content.ReadAsStringAsync();
            responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseDataStr);
            responseData.Should().Contain("token", expectedToken);
        }

        [Fact]
        public async Task Repeated_inactive_token_with_caching_enabled_should_hit_cache()
        {
            var expectedToken = "expected_token";
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Inactive);

            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = handler;
                o.SaveToken = true;
                o.EnableCaching = true;
                o.CacheDuration = TimeSpan.FromMinutes(10);
            }, true);

            client.SetBearerToken(expectedToken);

            var firstResponse = await client.GetAsync("http://test");

            firstResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            handler.SentIntrospectionRequest.Should().BeTrue();

            handler.SentIntrospectionRequest = false;
            var secondResponse = await client.GetAsync("http://test");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            handler.SentIntrospectionRequest.Should().BeFalse();
        }

        [Fact]
        public async Task ActiveToken_With_Discovery_Unavailable_On_First_Request()
        {
            var handler = new IntrospectionEndpointHandler(IntrospectionEndpointHandler.Behavior.Active);

            var client = PipelineFactory.CreateClient((o) =>
            {
                _options(o);
                o.BackchannelHttpHandler = handler;
            });

            client.SetBearerToken("sometoken");

            handler.IsDiscoveryFailureTest = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync("http://test"));
            
            handler.IsDiscoveryFailureTest = false;
            var result = await client.GetAsync("http://test");
            result.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}