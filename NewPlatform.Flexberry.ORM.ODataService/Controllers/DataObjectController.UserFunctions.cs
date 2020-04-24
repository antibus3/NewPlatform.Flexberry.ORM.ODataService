﻿namespace NewPlatform.Flexberry.ORM.ODataService.Controllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Net.Http;
    using System.Reflection;
    using System.Web.Http;
    using ICSSoft.STORMNET;
    using Microsoft.AspNet.OData.Extensions;
    using Microsoft.AspNet.OData.Routing;
    using Microsoft.OData;
    using Microsoft.OData.UriParser;
    using NewPlatform.Flexberry.ORM.ODataService.Formatter;
    using NewPlatform.Flexberry.ORM.ODataService.Functions;
    using NewPlatform.Flexberry.ORM.ODataService.Handlers;
    using NewPlatform.Flexberry.ORM.ODataService.Routing;

    using ODataPath = Microsoft.AspNet.OData.Routing.ODataPath;

    /// <summary>
    /// OData controller class.
    /// Part with OData Service functions.
    /// </summary>
    public partial class DataObjectController
    {
        /// <summary>
        /// The container with OData Service functions.
        /// </summary>
        private readonly IFunctionContainer _functions;

        /// <summary>
        /// Выполняет пользовательскую функцию.
        /// Имя "GetODataFunctionsExecute" устанавливается в <see cref="DataObjectRoutingConvention.SelectAction"/>.
        /// </summary>
        /// <returns>
        /// Результат выполнения пользовательской функции, преобразованный к типам сущностей EDM-модели или к примитивным типам.
        /// После преобразования создаётся результат HTTP для ответа.
        /// </returns>
        [CustomEnableQuery]
        public IHttpActionResult GetODataFunctionsExecute()
        {
            try
            {
                QueryOptions = CreateODataQueryOptions(typeof(DataObject));
                return ExecuteUserFunction(new QueryParameters(this));
            }
            catch (HttpResponseException ex)
            {
                if (HasOdataError(ex))
                {
                    return ResponseMessage(ex.Response);
                }
                else
                {
                    return ResponseMessage(InternalServerErrorMessage(ex));
                }
            }
            catch (TargetInvocationException ex)
            {
                if (HasOdataError(ex.InnerException))
                {
                    return ResponseMessage(((HttpResponseException)ex.InnerException)?.Response);
                }
                else
                {
                    return ResponseMessage(InternalServerErrorMessage(ex));
                }
            }
            catch (Exception ex)
            {
                return ResponseMessage(InternalServerErrorMessage(ex));
            }
        }

        /// <summary>
        /// Выполняет пользовательскую функцию.
        /// </summary>
        /// <param name="queryParameters">Параметры запроса.</param>
        /// <returns>Результат выполнения пользовательской функции, преобразованный к типам сущностей EDM-модели или к примитивным типам.</returns>
        internal IHttpActionResult ExecuteUserFunction(QueryParameters queryParameters)
        {
            queryParameters.Count = null;
            queryParameters.Request = Request;
            ODataPath odataPath = Request.ODataProperties().Path;

            // The OperationImportSegment type represents the Microsoft OData v5.7.0 UnboundFunctionPathSegment type here.
            OperationImportSegment segment = odataPath.Segments[odataPath.Segments.Count - 1] as OperationImportSegment;

            // The OperationImportSegment.Identifier property represents the Microsoft OData v5.7.0 UnboundFunctionPathSegment.FunctionName property here.
            if (segment == null || !_functions.IsRegistered(segment.Identifier))
                return SetResult("Function not found");

            Function function = _functions.GetFunction(segment.Identifier);
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            foreach (string parameterName in function.ParametersTypes.Keys)
            {
                try
                {
                    var parameterValue = segment.GetParameterValue(parameterName);
                    if (parameterValue is ODataEnumValue enumParameterValue)
                    {
                        parameterValue = Enum.Parse(function.ParametersTypes[parameterName], enumParameterValue.Value);
                    }

                    parameters.Add(parameterName, parameterValue);
                }
                catch (Exception ex)
                {
                    throw new ODataException($"Failed to convert parameter: {parameterName}", ex);
                }
            }

            var result = function.Handler(queryParameters, parameters);
            if (result == null)
            {
                return SetResult("Result is null.");
            }

            if (!(result is string) && result is IEnumerable)
            {
                Type type = null;
                if (result.GetType().IsGenericType)
                {
                    Type[] args = result.GetType().GetGenericArguments();
                    if (args.Length == 1)
                        type = args[0];
                }

                if (result.GetType().IsArray)
                {
                    type = result.GetType().GetElementType();
                }

                if (type != null && (type.IsSubclassOf(typeof(DataObject)) || type == typeof(DataObject)))
                {
                    var queryOpt = CreateODataQueryOptions(type);

                    QueryOptions = queryOpt;
                    if (QueryOptions.SelectExpand != null && QueryOptions.SelectExpand.SelectExpandClause != null)
                    {
                        Request.ODataProperties().SelectExpandClause = QueryOptions.SelectExpand.SelectExpandClause;
                    }

                    this.type = type;
                    CreateDynamicView();
                    IncludeCount = false;
                    if (queryOpt.Count != null && queryOpt.Count.Value)
                    {
                        IncludeCount = true;
                        if (queryParameters.Count != null)
                        {
                            Count = (int)queryParameters.Count;
                        }
                        else
                        {
                            Count = GetObjectsCount(type, queryOpt);
                        }
                    }

                    NameValueCollection queryParams = Request.RequestUri.ParseQueryString();

                    if ((_model.ExportService != null || _model.ODataExportService != null) && (Request.Properties.ContainsKey(PostPatchHandler.AcceptApplicationMsExcel) || Convert.ToBoolean(queryParams.Get("exportExcel"))))
                    {
                        _objs = (result as IEnumerable).Cast<DataObject>().ToArray();
                        return ResponseMessage(CreateExcel(queryParams));
                    }

                    var coll = GetEdmCollection((IEnumerable)result, type, 1, null, _dynamicView);
                    return SetResult(coll);
                }

                return SetResult(result);
            }

            if (result is DataObject)
            {
                QueryOptions = CreateODataQueryOptions(result.GetType());
                if (QueryOptions.SelectExpand != null && QueryOptions.SelectExpand.SelectExpandClause != null)
                {
                    Request.ODataProperties().SelectExpandClause = QueryOptions.SelectExpand.SelectExpandClause;
                }

                this.type = result.GetType();
                CreateDynamicView();
                var entityType = _model.GetEdmEntityType(this.type);
                return SetResult(GetEdmObject(entityType, result, 1, null, _dynamicView));
            }

            return SetResultPrimitive(result.GetType(), result);
        }
    }
}
