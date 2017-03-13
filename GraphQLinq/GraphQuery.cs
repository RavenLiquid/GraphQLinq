﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using Newtonsoft.Json.Linq;

namespace GraphQLinq
{
    public class GraphQuery<T> : IEnumerable<T>
    {
        private readonly GraphContext graphContext;
        private readonly GraphQueryBuilder<T> queryBuilder = new GraphQueryBuilder<T>();

        private readonly Lazy<string> lazyQuery;

        internal string QueryName { get; }
        internal LambdaExpression Selector { get; private set; }
        internal List<string> Includes { get; private set; } = new List<string>();
        internal Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();

        internal GraphQuery(GraphContext graphContext, string queryName)
        {
            QueryName = queryName;
            this.graphContext = graphContext;

            lazyQuery = new Lazy<string>(() => queryBuilder.BuildQuery(this, Includes));
        }

        public IEnumerator<T> GetEnumerator()
        {
            var query = lazyQuery.Value;

            return new GraphQueryEnumerator<T>(query, graphContext.BaseUrl);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public GraphQuery<T> Include<TProperty>(Expression<Func<T, TProperty>> path)
        {
            string include;
            if (!TryParsePath(path.Body, out include) || include == null)
            {
                throw new ArgumentException("Invalid Include Path Expression", nameof(path));
            }

            var graphQuery = Clone<T>();
            graphQuery.Includes.Add(include);

            return graphQuery;
        }

        public GraphQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> resultSelector)
        {
            if (resultSelector.NodeType != ExpressionType.Lambda)
            {
                throw new ArgumentException($"{resultSelector} must be lambda expression", nameof(resultSelector));
            }

            var graphQuery = Clone<TResult>();

            graphQuery.Selector = resultSelector;

            return graphQuery;
        }

        public override string ToString()
        {
            return lazyQuery.Value;
        }

        private GraphQuery<TR> Clone<TR>()
        {
            return new GraphQuery<TR>(graphContext, QueryName) { Arguments = Arguments, Selector = Selector, Includes = Includes.ToList() };
        }

        private static bool TryParsePath(Expression expression, out string path)
        {
            path = null;
            var withoutConvert = expression.RemoveConvert(); // Removes boxing
            var memberExpression = withoutConvert as MemberExpression;
            var callExpression = withoutConvert as MethodCallExpression;

            if (memberExpression != null)
            {
                var thisPart = memberExpression.Member.Name;
                string parentPart;
                if (!TryParsePath(memberExpression.Expression, out parentPart))
                {
                    return false;
                }
                path = parentPart == null ? thisPart : (parentPart + "." + thisPart);
            }
            else if (callExpression != null)
            {
                if (callExpression.Method.Name == "Select"
                    && callExpression.Arguments.Count == 2)
                {
                    string parentPart;
                    if (!TryParsePath(callExpression.Arguments[0], out parentPart))
                    {
                        return false;
                    }
                    if (parentPart != null)
                    {
                        var subExpression = callExpression.Arguments[1] as LambdaExpression;
                        if (subExpression != null)
                        {
                            string thisPart;
                            if (!TryParsePath(subExpression.Body, out thisPart))
                            {
                                return false;
                            }
                            if (thisPart != null)
                            {
                                path = parentPart + "." + thisPart;
                                return true;
                            }
                        }
                    }
                }
                return false;
            }

            return true;
        }
    }

    class GraphQueryBuilder<T>
    {
        private const string QueryTemplate = @"{{ {0}: {1} {2} {{ {3} }}}}";
        internal const string ItemAlias = "item";
        internal const string ResultAlias = "result";

        public string BuildQuery(GraphQuery<T> graphQuery, List<string> includes)
        {
            var selectClause = "";

            if (graphQuery.Selector != null)
            {
                var body = graphQuery.Selector.Body;

                if (body.NodeType == ExpressionType.MemberAccess)
                {
                    selectClause = $"{ItemAlias}: {((MemberExpression)body).Member.Name}";
                }

                if (body.NodeType == ExpressionType.New)
                {
                    var newExpression = (NewExpression)body;    

                    var queryFields = newExpression.Members.Zip(newExpression.Arguments,
                        (memberInfo, expression) => new { Alias = memberInfo.Name, ((MemberExpression)expression).Member.Name });

                    selectClause = string.Join(" ", queryFields.Select(arg => arg.Alias + ": " + arg.Name));
                }
            }
            else
            {
                selectClause = BuildSelectClauseForType(typeof(T), includes);
            }

            //(type: [STANDARD_CHARGER, STORE], openSoon: true)
            var argList = graphQuery.Arguments.Where(pair => pair.Value != null).Select(pair =>
            {
                var value = pair.Value.ToString();

                if (pair.Value is bool)
                {
                    value = value.ToLowerInvariant();
                }

                var enumerable = pair.Value as IEnumerable;
                if (enumerable != null)
                {
                    value = $"[{string.Join(", ", enumerable.Cast<object>())}]";
                }

                return $"{pair.Key}: {value}";
            });

            var args = string.Join(", ", argList);
            var argsWithParentheses = string.IsNullOrEmpty(args) ? "" : $"({args})";

            return string.Format(QueryTemplate, ResultAlias, graphQuery.QueryName.ToLower(), argsWithParentheses, selectClause);
        }

        private static string BuildSelectClauseForType(Type targetType, int depth = 1)
        {
            var propertyInfos = targetType.GetProperties();

            var propertiesToInclude = propertyInfos.Where(info => !info.PropertyType.HasNestedProperties());

            var selectClause = string.Join(Environment.NewLine, propertiesToInclude.Select(info => new string(' ', depth * 2) + info.Name));

            return selectClause;
        }

        private static string BuildSelectClauseForType(Type targetType, IEnumerable<string> includes)
        {
            var selectClause = BuildSelectClauseForType(targetType);

            foreach (var include in includes)
            {
                var fieldsFromInclude = BuildSelectClauseForInclude(targetType, include);
                selectClause = selectClause + Environment.NewLine + fieldsFromInclude;
            }

            return Environment.NewLine + selectClause + Environment.NewLine;
        }

        private static string BuildSelectClauseForInclude(Type targetType, string include, int depth = 1)
        {
            if (string.IsNullOrEmpty(include))
            {
                return BuildSelectClauseForType(targetType, depth);
            }
            var leftPadding = new string(' ', depth * 2);

            var dotIndex = include.IndexOf(".", StringComparison.InvariantCultureIgnoreCase);

            var restOfTheIncludePath = dotIndex >= 0 ? include.Substring(dotIndex + 1) : "";
            var currentPropertyName = dotIndex >= 0 ? include.Substring(0, dotIndex) : include;

            var propertyType = targetType.GetProperty(currentPropertyName).PropertyType.GetTypeOrListType();

            if (propertyType.IsPrimitiveOrString())
            {
                return leftPadding + currentPropertyName;
            }

            var fieldsFromInclude = BuildSelectClauseForInclude(propertyType, restOfTheIncludePath, depth + 1);
            fieldsFromInclude = $"{leftPadding}{currentPropertyName} {{{Environment.NewLine}{fieldsFromInclude}{Environment.NewLine}{leftPadding}}}";
            return fieldsFromInclude;
        }
    }

    class GraphQueryEnumerator<T> : IEnumerator<T>
    {
        private IEnumerator<T> listEnumerator;

        private readonly string query;
        private readonly string baseUrl;

        private const string DataPathPropertyName = "data";
        private const string ErrorPathPropertyName = "errors";

        private static readonly bool HasNestedProperties = typeof(T).HasNestedProperties();

        public GraphQueryEnumerator(string query, string baseUrl)
        {
            this.query = query;
            this.baseUrl = baseUrl;
        }

        public void Dispose()
        {
            listEnumerator.Dispose();
        }

        public bool MoveNext()
        {
            if (listEnumerator == null)
            {
                listEnumerator = DownloadData().GetEnumerator();
            }

            return listEnumerator.MoveNext();
        }

        private IEnumerable<T> DownloadData()
        {
            using (var webClient = new WebClient())
            {
                webClient.Headers.Add("Content-Type", "application/graphql");

                var json = webClient.UploadString(baseUrl, query);

                var jObject = JObject.Parse(json);

                if (jObject.SelectToken(ErrorPathPropertyName) != null)
                {
                    var errors = jObject[ErrorPathPropertyName].ToObject<List<GraphQueryError>>();
                    throw new GraphQueryExecutionException(errors, query);
                }

                var enumerable = jObject[DataPathPropertyName][GraphQueryBuilder<T>.ResultAlias]
                                .Select(token => (HasNestedProperties ? token : token[GraphQueryBuilder<T>.ItemAlias]).ToObject<T>());

                return enumerable;
            }
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public T Current => listEnumerator.Current;

        object IEnumerator.Current => Current;
    }

    static class TypeExtensions
    {
        internal static bool IsPrimitiveOrString(this Type type)
        {
            return type.IsPrimitive || type == typeof(string);
        }

        internal static bool HasNestedProperties(this Type type)
        {
            var trueType = GetTypeOrListType(type);

            return !IsPrimitiveOrString(trueType);
        }

        internal static Type GetTypeOrListType(this Type type)
        {
            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var genericArguments = type.GetGenericArguments();

                return genericArguments[0];
            }

            return type;
        }

        internal static Expression RemoveConvert(this Expression expression)
        {
            while ((expression != null)
                   && (expression.NodeType == ExpressionType.Convert
                       || expression.NodeType == ExpressionType.ConvertChecked))
            {
                expression = RemoveConvert(((UnaryExpression)expression).Operand);
            }

            return expression;
        }
    }

    public class GraphQueryError
    {
        public string Message { get; set; }
        public ErrorLocation[] Locations { get; set; }
    }

    public class ErrorLocation
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public class GraphQueryExecutionException : Exception
    {
        public GraphQueryExecutionException(IEnumerable<GraphQueryError> errors, string query)
            : base($"One or more errors occured during query execution. Check {nameof(Errors)} property for details")
        {
            Errors = errors;
            GraphQLQuery = query;
        }

        public string GraphQLQuery { get; private set; }
        public IEnumerable<GraphQueryError> Errors { get; private set; }
    }
}