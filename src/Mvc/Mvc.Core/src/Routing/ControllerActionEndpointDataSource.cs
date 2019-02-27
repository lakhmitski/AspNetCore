// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Microsoft.AspNetCore.Mvc.Routing
{
    internal class ControllerActionEndpointDataSource : ActionEndpointDataSourceBase
    {
        private readonly ActionEndpointFactory _endpointFactory;
        private readonly RoutePatternTransformer _transformer;

        private readonly List<ConventionalRouteEntry> _routes;

        public ControllerActionEndpointDataSource(
            IActionDescriptorCollectionProvider actions, 
            ActionEndpointFactory endpointFactory,
            RoutePatternTransformer transformer)
            : base(actions)
        {
            _endpointFactory = endpointFactory;
            _transformer = transformer;
            
            _routes = new List<ConventionalRouteEntry>();

            // IMPORTANT: this needs to be the last thing we do in the constructor. 
            // Change notifications can happen immediately!
            Subscribe();
        }

        public void AddRoute(in ConventionalRouteEntry route)
        {
            lock (Lock)
            {
                _routes.Add(route);
            }
        }

        protected override List<Endpoint> CreateEndpoints(IReadOnlyList<ActionDescriptor> actions, IReadOnlyList<Action<EndpointBuilder>> conventions)
        {
            var endpoints = new List<Endpoint>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // For each controller action - add the relevant endpoints.
            //
            // 1. If the action is attribute routed, we use that information verbatim
            // 2. If the action is conventional routed
            //      a. Create a *matching only* endpoint for each action X route (if possible)
            //      b. Ignore link generation for now
            for (var i = 0; i < actions.Count; i++)
            {
                if (actions[i] is ControllerActionDescriptor action)
                {
                    _endpointFactory.AddEndpoints(endpoints, action, _routes, conventions);

                    if (_routes.Count > 0)
                    {
                        // If we have conventional routes, keep track of the keys so we can create
                        // the link generation routes later.
                        foreach (var kvp in action.RouteValues)
                        {
                            keys.Add(kvp.Key);
                        }
                    }
                }
            }

            // Now create a *link generation only* endpoint for each route. This gives us a very
            // compatible experience to previous versions.
            for (var i = 0; i < _routes.Count; i++)
            {
                var route = _routes[i];

                var requiredValues = new RouteValueDictionary();
                foreach (var key in keys)
                {
                    if (route.Pattern.GetParameter(key) != null)
                    {
                        // Parameter (allow any)
                        requiredValues[key] = RoutePattern.RequiredValueAny;
                    }
                    else if (route.Pattern.Defaults.TryGetValue(key, out var value))
                    {
                        requiredValues[key] = value;
                    }
                    else
                    {
                        requiredValues[key] = null;
                    }
                }                

                // We have to do some massaging of the pattern to try and get the
                // required values to be correct.
                endpoints.Add(new RouteEndpoint(
                    context => Task.CompletedTask,
                    _transformer.SubstituteRequiredValues(route.Pattern, requiredValues),
                    1 + (i * 1), 
                    new EndpointMetadataCollection(new SuppressMatchingMetadata()),
                    "Route: " + route.Pattern.RawText));
            }

            return endpoints;
        }
    }
}

