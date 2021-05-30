using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;


namespace EntitiesXrm
{
	#region XrmServiceContext

	/// <summary>
	/// Represents a source of entities bound to a CRM service. It tracks and manages changes made to the retrieved entities.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public partial class XrmServiceContext : OrganizationServiceContext
	{
		/// <summary>
		/// Constructor.
		/// </summary>
		public XrmServiceContext(IOrganizationService service) :
				base(service)
		{
		}

		/// <summary>
		/// Gets a binding to the set of all <see cref="Escuela"/> entities.
		/// </summary>
		public System.Linq.IQueryable<Escuela> EscuelaSet
		{
			get
			{
				return this.CreateQuery<Escuela>();
			}
		}
		/// <summary>
		/// Gets a binding to the set of all <see cref="Profesor"/> entities.
		/// </summary>
		public System.Linq.IQueryable<Profesor> ProfesorSet
		{
			get
			{
				return this.CreateQuery<Profesor>();
			}
		}
		/// <summary>
		/// Gets a binding to the set of all <see cref="Ejecuciondeactualizacion"/> entities.
		/// </summary>
		public System.Linq.IQueryable<Ejecuciondeactualizacion> EjecuciondeactualizacionSet
		{
			get
			{
				return this.CreateQuery<Ejecuciondeactualizacion>();
			}
		}
		/// <summary>
		/// Gets a binding to the set of all <see cref="Expediente"/> entities.
		/// </summary>
		public System.Linq.IQueryable<Expediente> ExpedienteSet
		{
			get
			{
				return this.CreateQuery<Expediente>();
			}
		}
		/// <summary>
		/// Gets a binding to the set of all <see cref="Tarea"/> entities.
		/// </summary>
		public System.Linq.IQueryable<Tarea> TareaSet
		{
			get
			{
				return this.CreateQuery<Tarea>();
			}
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class LinqEntityLimiter : ExpressionVisitor
	{
		protected override Expression VisitNew(NewExpression node)
		{
			var constructor = node.Constructor;
			var parameters = constructor.GetParameters();
			var param = node.Arguments.FirstOrDefault();
			var paramType = param?.Type;

			if (paramType != null && constructor.DeclaringType == paramType
				&& parameters.FirstOrDefault()?.ParameterType == typeof(object))
			{
				var valueGetter = paramType.GetMethod("GetAttributeValue", new[] { typeof(string) })?
					.MakeGenericMethod(typeof(object));

				if (valueGetter != null)
				{
					var limiterType = (node.Arguments.Skip(1).FirstOrDefault() as ConstantExpression)?.Value as Type;

					NewArrayExpression keysInit = null;
					NewArrayExpression valuesInit = null;

					if (limiterType == null && (parameters.Length == 1 || parameters.Skip(1).FirstOrDefault()?.ParameterType == typeof(Type)))
					{
						var attributes = paramType.GetProperties().Cast<MemberInfo>().Union(paramType.GetFields())
							.Where(e => Attribute.IsDefined(e, typeof(AttributeLogicalNameAttribute))).ToArray();

						keysInit = Expression.NewArrayInit(typeof(string), attributes
							.Select(e => Expression.Constant(e.GetCustomAttribute<AttributeLogicalNameAttribute>().LogicalName)));
						valuesInit = Expression.NewArrayInit(typeof(object), attributes
							.Select(e => Expression.Call(param, valueGetter,
								Expression.Constant(e.GetCustomAttribute<AttributeLogicalNameAttribute>().LogicalName))));
					}
					else if (limiterType != null && typeof(EntityContract).IsAssignableFrom(limiterType)
						&& parameters.Skip(1).FirstOrDefault()?.ParameterType == typeof(Type))
					{
						var attributes = limiterType.GetProperties().Cast<MemberInfo>().Union(limiterType.GetFields())
							.Where(e => Attribute.IsDefined(e, typeof(CrmFieldMappingAttribute))
								&& !Attribute.IsDefined(e, typeof(CrmRelationMappingAttribute))).ToArray();

						keysInit = Expression.NewArrayInit(typeof(string), attributes
							.Select(e => Expression.Constant(e.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName)));
						valuesInit = Expression.NewArrayInit(typeof(object), attributes
							.Select(e => Expression.Call(param, valueGetter,
								Expression.Constant(e.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName))));
					}

					if (keysInit != null)
					{
						var constructorInfo = paramType.GetConstructor(new[] { typeof(string[]), typeof(object[]) });

						if (constructorInfo != null)
						{
							return Expression.New(constructorInfo, keysInit, valuesInit);
						}
					}
				}
			}

			return base.VisitNew(node);
		}
	}

	/// <summary>
	/// Credits: https://github.com/davidfowl/QueryInterceptor
	/// </summary>
	public static class QueryableExtensions
	{
		public static IQueryable<T> InterceptWith<T>(this IQueryable<T> source, params ExpressionVisitor[] visitors)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			return new QueryTranslator<T>(source, visitors);
		}
	}

	internal class QueryTranslator<T> : IOrderedQueryable<T>
	{
		public Type ElementType => typeof(T);
		public Expression Expression { get; }
		public IQueryProvider Provider => provider;

		private readonly QueryTranslatorProvider<T> provider;

		public QueryTranslator(IQueryable source, IEnumerable<ExpressionVisitor> visitors)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			if (visitors == null)
			{
				throw new ArgumentNullException(nameof(visitors));
			}

			Expression = Expression.Constant(this);
			provider = new QueryTranslatorProvider<T>(source, visitors);
		}

		public QueryTranslator(IQueryable source, Expression expression, IEnumerable<ExpressionVisitor> visitors)
		{
			Expression = expression ?? throw new ArgumentNullException(nameof(expression));
			provider = new QueryTranslatorProvider<T>(source, visitors);
		}

		public IEnumerator<T> GetEnumerator()
		{
			return ((IEnumerable<T>)provider.ExecuteEnumerable(Expression)).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return provider.ExecuteEnumerable(Expression).GetEnumerator();
		}
	}

	internal abstract class QueryTranslatorProvider : ExpressionVisitor
	{
		internal IQueryable Source { get; }

		protected QueryTranslatorProvider(IQueryable source)
		{
			Source = source ?? throw new ArgumentNullException(nameof(source));
		}
	}

	internal class QueryTranslatorProvider<T> : QueryTranslatorProvider, IQueryProvider
	{
		private readonly IEnumerable<ExpressionVisitor> visitors;

		public QueryTranslatorProvider(IQueryable source, IEnumerable<ExpressionVisitor> visitors)
			: base(source)
		{
			this.visitors = visitors ?? throw new ArgumentNullException(nameof(visitors));
		}

		public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			return new QueryTranslator<TElement>(Source, expression, visitors);
		}

		public IQueryable CreateQuery(Expression expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			var elementType = expression.Type.GetGenericArguments().First();
			var result = (IQueryable)Activator.CreateInstance(typeof(QueryTranslator<>).MakeGenericType(elementType),
				Source, expression, visitors);

			return result;
		}

		public TResult Execute<TResult>(Expression expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			var result = (this as IQueryProvider).Execute(expression);

			return (TResult)result;
		}

		public object Execute(Expression expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			var translated = VisitAll(expression);

			return Source.Provider.Execute(translated);
		}

		internal IEnumerable ExecuteEnumerable(Expression expression)
		{
			if (expression == null)
			{
				throw new ArgumentNullException(nameof(expression));
			}

			var translated = VisitAll(expression);

			return Source.Provider.CreateQuery(translated);
		}

		private Expression VisitAll(Expression expression)
		{
			// Run all visitors in order
			var visitorsQ = new ExpressionVisitor[] { this }.Concat(visitors);
			return visitorsQ.Aggregate(expression, (expr, visitor) => visitor.Visit(expr));
		}

		protected override Expression VisitConstant(ConstantExpression node)
		{
			// Fix up the Expression tree to work with the underlying LINQ provider
			if (!node.Type.IsGenericType || node.Type.GetGenericTypeDefinition() != typeof(QueryTranslator<>))
			{
				return base.VisitConstant(node);
			}

			var provider = ((IQueryable)node.Value).Provider as QueryTranslatorProvider;

			return provider == null ? Source.Expression : provider.Source.Expression;
		}
	}

	#endregion

	#region Extensions

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class ValidationError
	{
		public Guid? RecordId;
		public string AttributeName;

		public ValidationType ErrorType;
		public IDictionary<int, string> ErrorMessage;

		public string StringValue;
		public int? StringMaxLength;

		public decimal? NumericValue;
		public decimal? NumericRangeMin;
		public decimal? NumericRangeMax;
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class ValidationLocalisation
	{
		public ValidationType ErrorType;
		/// <summary>
		/// A dictionary of language code as key and message format as value
		/// </summary>
		public IDictionary<int, string> Localisation;
	}

	public static partial class CrmGeneratorExtensions
	{
		private class RelationProperty
		{
			internal PropertyInfo Property;
			internal RelationshipSchemaNameAttribute RelationAttribute;
		}

		private static IDictionary<string, IEnumerable<RelationProperty>> relationPropertyCache =
			new ConcurrentDictionary<string, IEnumerable<RelationProperty>>();

		public static IEnumerable<TEntity> ProcessFetchXmlRelations<TEntity>(this IEnumerable<TEntity> entitiesParam, string fetchXml)
			where TEntity : GeneratedEntityBase
		{
			if (string.IsNullOrWhiteSpace(fetchXml))
			{
				throw new ArgumentNullException(nameof(fetchXml));
			}

			if (entitiesParam == null)
			{
				throw new ArgumentNullException(nameof(entitiesParam));
			}

			var entities = entitiesParam.ToArray();

			if (!entities.Any())
			{
				return new TEntity[0];
			}

			foreach (var entity in entities)
			{
				var depth = 0;
				ProcessEntity(entity, entity, XElement.Parse(fetchXml).Element("entity"), ref depth);
			}

			return entities.GroupBy(e => e.Id).Select(ConsolidateEntity).Where(e => e?.Id != Guid.Empty);
		}

		private static void ProcessEntity(Entity baseEntity, GeneratedEntityBase targetEntity, XElement xmlElement,
			ref int depth, string intersectingEntity = null)
		{
			foreach (var xLink in xmlElement.Elements("link-entity"))
			{
				depth++;

				var linkedName = xLink.Attribute("name")?.Value;
				var from = xLink.Attribute("from")?.Value;
				var to = xLink.Attribute("to")?.Value;
				var alias = GetLinkAlias(xLink, depth);

				if (string.IsNullOrWhiteSpace(linkedName))
				{
					throw new ArgumentNullException(nameof(linkedName), "Linked entity name could not be found in FetchXML.");
				}

				if (string.IsNullOrWhiteSpace(from))
				{
					throw new ArgumentNullException(nameof(from),
						$"'from' value could not be found in FetchXML for '{linkedName}'.");
				}

				if (string.IsNullOrWhiteSpace(to))
				{
					throw new ArgumentNullException(nameof(to),
						$"'to' value could not be found in FetchXML for '{linkedName}'.");
				}

				if (string.IsNullOrWhiteSpace(alias))
				{
					throw new ArgumentNullException(nameof(alias),
						$"'alias' value could not be found in FetchXML for '{linkedName}'.");
				}

				var relationParameters = targetEntity.RelationProperties
					.Select(p => p.Value)
					.Where(p => p.Length >= 5)
					.FirstOrDefault(p => p[1].ToString() == linkedName && p[3].ToString() == from && p[4].ToString() == to);
				var isNn = false;

				if (relationParameters == null)
				{
					// intersecting
					relationParameters = targetEntity.RelationProperties
						.Select(p => p.Value)
						.Where(p => p.Length >= 9)
						.FirstOrDefault(p => linkedName == p[2].ToString());
					isNn = true;

					if (relationParameters == null)
					{
						relationParameters = targetEntity.RelationProperties
							.Select(p => p.Value)
							.Where(p => p.Length >= 3)
							.FirstOrDefault(p => p[2].ToString() == intersectingEntity);
						isNn = false;

						if (relationParameters == null)
						{
							continue;
						}
					}
				}

				var role = (relationParameters[8] as Type)?.IsArray == true ? EntityRole.Referenced : EntityRole.Referencing;
				var schemaName = relationParameters[7].ToString();

				var relationProperties = GetRelationInfoCache(targetEntity);

				var relationProperty = relationProperties.FirstOrDefault(pa =>
					pa.RelationAttribute.SchemaName == schemaName
						&& (pa.RelationAttribute.PrimaryEntityRole == role || pa.RelationAttribute.PrimaryEntityRole == null))?
					.Property;
				var propertyType = relationProperty?.PropertyType;

				if (propertyType == null)
				{
					continue;
				}

				var relatedEntity = PrepareRelation(targetEntity, relationProperty);

				if (relatedEntity == null)
				{
					continue;
				}

				foreach (var xAttribute in xLink.Elements("attribute"))
				{
					ProcessXAttribute(baseEntity, relatedEntity, alias, xAttribute);
				}

				if (xLink.Elements("all-attributes").Any())
				{
					ProcessAllAttributes(baseEntity, relatedEntity, alias);
				}

				// go over the related entity as well for its own relations
				ProcessEntity(baseEntity, isNn ? targetEntity : relatedEntity, xLink, ref depth, isNn ? linkedName : null);
			}
		}

		private static IEnumerable<RelationProperty> GetRelationInfoCache(GeneratedEntityBase targetEntity)
		{
			if (!relationPropertyCache.TryGetValue(targetEntity.LogicalName, out var relationProperties))
			{
				relationProperties = relationPropertyCache[targetEntity.LogicalName] =
					targetEntity.GetType().GetProperties()
						.Where(p => Attribute.IsDefined(p, typeof(RelationshipSchemaNameAttribute)))
						.Select(p =>
							new RelationProperty
							{
								Property = p,
								RelationAttribute = p.GetCustomAttribute<RelationshipSchemaNameAttribute>()
							});
			}

			return relationProperties;
		}

		private static string GetLinkAlias(XElement xLink, int depth)
		{
			var linkedName = xLink.Attribute("name")?.Value;

			if (string.IsNullOrWhiteSpace(linkedName))
			{
				return null;
			}

			var alias = xLink.Attribute("alias")?.Value;

			if (string.IsNullOrWhiteSpace(alias))
			{
				alias = $"{linkedName}{depth}";
			}

			return alias;
		}

		private static GeneratedEntityBase PrepareRelation(GeneratedEntityBase targetEntity, PropertyInfo relationProperty)
		{
			var propertyType = relationProperty?.PropertyType;

			if (propertyType == null)
			{
				return null;
			}

			var isArray = typeof(IEnumerable).IsAssignableFrom(propertyType);
			var relatedEntityType = propertyType;

			if (isArray)
			{
				relatedEntityType = propertyType.GetElementType();
			}

			if (relatedEntityType == null)
			{
				return null;
			}

			var relatedEntity = Activator.CreateInstance(relatedEntityType) as GeneratedEntityBase;

			if (relatedEntity == null)
			{
				return null;
			}

			object relatedValue = relatedEntity;
			var currentValue = relationProperty.GetValue(targetEntity);

			if (isArray)
			{
				var relatedList = (object[])(currentValue ?? Activator.CreateInstance(propertyType, 1));
				relatedList[0] = relatedEntity;
				relatedValue = relatedList;
			}
			else if (currentValue != null)
			{
				// already parsed N-1
				return null;
			}

			relationProperty.SetValue(targetEntity, relatedValue);

			return relatedEntity;
		}

		private static void ProcessXAttribute(Entity baseEntity, GeneratedEntityBase relatedEntity, string relatedAlias,
			XElement xAttribute)
		{
			var attributeName = xAttribute.Attribute("name")?.Value;
			var attributeAlias = xAttribute.Attribute("alias")?.Value;
			var isAliasedSame = string.IsNullOrWhiteSpace(relatedAlias) || attributeName == attributeAlias;

			if (!string.IsNullOrWhiteSpace(attributeAlias))
			{
				attributeName = attributeAlias;
			}

			ProcessAttribute(baseEntity, relatedEntity, relatedAlias, attributeName, isAliasedSame);
		}

		private static void ProcessAttribute(Entity baseEntity, GeneratedEntityBase relatedEntity, string relatedAlias,
			string attributeName, bool isAliasedSame)
		{
			var attribute = baseEntity.Attributes
				.FirstOrDefault(a => !string.IsNullOrWhiteSpace(relatedAlias) && a.Key == $"{relatedAlias}.{attributeName}").Value
				?? baseEntity.Attributes
					.FirstOrDefault(a => isAliasedSame && a.Key == attributeName).Value;

			if (attribute is AliasedValue)
			{
				var aliasedValue = attribute as AliasedValue;
				relatedEntity[aliasedValue.AttributeLogicalName] = aliasedValue.Value;
				return;
			}

			if (attribute != null)
			{
				relatedEntity[attributeName] = attribute;
			}
		}

		private static void ProcessAllAttributes(Entity baseEntity, GeneratedEntityBase relatedEntity, string relatedAlias)
		{
			var attributes = baseEntity.Attributes
				.Where(a =>
					string.IsNullOrWhiteSpace(relatedAlias)
						? !a.Key.Contains($".")
						: a.Key.StartsWith($"{relatedAlias}."));

			foreach (var pair in attributes)
			{
				var attribute = pair.Value;

				AliasedValue aliasedValue;

				if (attribute is AliasedValue)
				{
					aliasedValue = attribute as AliasedValue;
					relatedEntity[aliasedValue.AttributeLogicalName] = aliasedValue.Value;
					continue;
				}

				if (attribute != null)
				{
					relatedEntity[pair.Key] = attribute;
				}
			}
		}

		private static TEntity ConsolidateEntity<TEntity>(IGrouping<Guid, TEntity> grouping)
			where TEntity : GeneratedEntityBase
		{
			if (!grouping.Any())
			{
				return null;
			}

			var baseEntity = grouping.First();

			var relationProperties = GetRelationInfoCache(baseEntity);
			var baseRelationProperties = relationProperties
				.Where(x => x.Property.PropertyType.IsArray
					&& (x.RelationAttribute.PrimaryEntityRole == EntityRole.Referenced
						|| x.RelationAttribute.PrimaryEntityRole == null)).ToArray();

			foreach (var entity in grouping.Skip(1))
			{
				foreach (var relationProperty in baseRelationProperties)
				{
					var currentBaseRelationInfo = baseRelationProperties
						.FirstOrDefault(x => x.RelationAttribute.SchemaName == relationProperty.RelationAttribute.SchemaName)?
						.Property;
					var currentRelation = (GeneratedEntityBase[])relationProperty.Property.GetValue(entity);

					if (currentBaseRelationInfo == null || currentRelation == null)
					{
						relationProperty.Property.SetValue(baseEntity, null);
						continue;
					}

					var currentBaseRelation = ((GeneratedEntityBase[])currentBaseRelationInfo.GetValue(baseEntity))?
						.Where(e => e?.Id != Guid.Empty).ToArray();

					if (currentBaseRelation == null || !currentBaseRelation.Any())
					{
						currentBaseRelationInfo.SetValue(baseEntity, null);
						continue;
					}

					var baseLength = currentBaseRelation?.Length ?? 0;

					var newBaseRelation = (GeneratedEntityBase[])Activator.CreateInstance(currentRelation.GetType(), baseLength + 1);

					if (currentBaseRelation?.Length > 0)
					{
						Array.Copy(currentBaseRelation, newBaseRelation, baseLength);
					}

					Array.Copy(currentRelation.Where(e => e?.Id != Guid.Empty).ToArray(), 0, newBaseRelation, baseLength, 1);
					currentBaseRelationInfo.SetValue(baseEntity, newBaseRelation);
				}
			}

			foreach (var baseRelationPropertyInfo in baseRelationProperties)
			{
				var baseRelationProperty = baseRelationPropertyInfo.Property;

				var currentRelation = (GeneratedEntityBase[])baseRelationProperty.GetValue(baseEntity);
				var currentRelationFiltered = currentRelation?.Where(e => e?.Id != Guid.Empty).ToArray();

				if (currentRelationFiltered == null || !currentRelationFiltered.Any())
				{
					baseRelationProperty.SetValue(baseEntity, null);
					continue;
				}

				var related = currentRelation.GroupBy(e => e.Id).Select(ConsolidateEntity)
					.Where(e => e?.Id != Guid.Empty).ToArray();

				if (related == null || !related.Any())
				{
					baseRelationProperty.SetValue(baseEntity, null);
					continue;
				}

				var newRelation = (GeneratedEntityBase[])Activator.CreateInstance(currentRelation.GetType(), related.Length);
				Array.Copy(related, newRelation, related.Length);
				baseRelationProperty.SetValue(baseEntity, newRelation);
			}

			return baseEntity;
		}

		/// <summary>
		/// Converts an enum constant to an OptionSetValue object..
		/// </summary>
		/// <param name="enumMember">The early-bound enum member constant; e.g.: 'Account.IndustryCode.Accounting'</param>
		/// <returns>The OptionSetValue object.</returns>
		public static OptionSetValue ToOptionSetValue(this Enum enumMember)
		{
			return new OptionSetValue(int.Parse(enumMember.ToString("d")));
		}

		/// <summary>
		///     Calling this method on a LINQ will result in limiting attributes retrieved from CRM on any early-bound entity in
		///     the query.<br />
		///     The properties defined in the entity's class will be the ones retrieved from CRM by default; all other attributes
		///     in CRM will be ignored.<br />
		///     The entity <b>must</b> be passed to a constructor of the same type.<br />
		///     Example:
		///     <code>
		/// (from account in new XrmServiceContext(service).AccountSet
		/// select new Account(account)).ToList()
		/// </code>
		/// </summary>
		public static IQueryable<TEntity> LimitAttributes<TEntity>(this IQueryable<TEntity> q) where TEntity : Entity
		{
			return q.InterceptWith(new LinqEntityLimiter());
		}

		/// <summary>
		///     Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///     Validation checks: required, numeric value range, and string length.<br />
		/// </summary>
		/// <param name="validationTypes">An array of validations to run. Default is all.</param>
		/// <returns>A single message containing all validation errors.</returns>
		public static string GetValidationErrorsMessage(this Entity entityRecord, ValidationType[] validationTypes)
		{
			return GetValidationErrorsMessage(entityRecord,
				validationLocationsDefaults.Where(local => validationTypes.Contains(local.ErrorType)).ToArray());
		}

		/// <summary>
		///     Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///     Validation checks: required, numeric value range, and string length.<br />
		/// </summary>
		/// <param name="validationLocalisation">
		///     [OPTIONAL] An array containing validation types with the message format to use for the validation error.<br />
		///     For validation type "Range", the message format takes three params for min, max, and actual value respectively;
		///         e.g.: "Value must be between {0} and {1}. Actual value was {2}.".<br />
		///     For validation type "MaxLength", the message format takes two params;
		///		    e.g.: "Value length must be less than {0}. Actual value was {1}."<br />
		///     Only include ONE localisation language.<br />
		///     Default is a check on all validation types.
		/// </param>
		/// <returns>A single message containing all validation errors.</returns>
		public static string GetValidationErrorsMessage(this Entity entityRecord, ValidationLocalisation[] validationLocalisation = null)
		{
			if (validationLocalisation != null
				&& validationLocalisation.Any(local => local.Localisation.Count > 1))
			{
				throw new ArgumentOutOfRangeException("validationLocalisation",
					"There should only be one validation localisation language for this method.");
			}

			var validationErrorMessages = GetValidationErrorMessages(entityRecord, validationLocalisation);

			if (validationErrorMessages.Any())
			{
				return validationErrorMessages.Aggregate(
					(message1, message2) => message1 + "\r\n-----------------------\r\n" + message2);
			}

			return null;
		}

		private static readonly ValidationLocalisation[] validationLocationsDefaults =
		{
			new ValidationLocalisation
			{
				ErrorType = ValidationType.Required,
				Localisation = new Dictionary<int, string>
							   {
								   {1033, "Value cannot be null."}
							   }
			},
			new ValidationLocalisation
			{
				ErrorType = ValidationType.Range,
				Localisation = new Dictionary<int, string>
							   {
								   {1033, @"Attribute value must be between ""{0}"" and ""{1}"", inclusive. Actual value was ""{2}""."}
							   }
			},
			new ValidationLocalisation
			{
				ErrorType = ValidationType.MaxLength,
				Localisation = new Dictionary<int, string>
							   {
								   {1033, @"Attribute length must be less than ""{0}"". Actual attribute value was ""{1}""."}
							   }
			}
		};

		/// <summary>
		///     Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///     Validation checks: required, numeric value range, and string length.<br />
		/// </summary>
		/// <param name="validationTypes">An array of validations to run.</param>
		/// <returns>A list of messages indicating validation errors.</returns>
		public static List<string> GetValidationErrorMessages(this Entity entityRecord, ValidationType[] validationTypes)
		{
			return GetValidationErrorMessages(entityRecord,
				validationLocationsDefaults.Where(local => validationTypes.Contains(local.ErrorType)).ToArray());
		}

		/// <summary>
		///     Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///     Validation checks: required, numeric value range, and string length.<br />
		/// </summary>
		/// <param name="validationLocalisation">
		///     [OPTIONAL] An array containing validation types with the message format to use for the validation error.<br />
		///     For validation type "Range", the message format takes three params for min, max, and actual value respectively;
		///         e.g.: "Value must be between {0} and {1}. Actual value was {2}.".<br />
		///     For validation type "MaxLength", the message format takes two params;
		///		    e.g.: "Value length must be less than {0}. Actual value was {1}."<br />
		///     Only include ONE localisation language.<br />
		///     Default is a check on all validation types.
		/// </param>
		/// <returns>A list of messages indicating validation errors.</returns>
		public static List<string> GetValidationErrorMessages(this Entity entityRecord, ValidationLocalisation[] validationLocalisation = null)
		{
			if (validationLocalisation != null
				&& validationLocalisation.Any(local => local.Localisation.Count > 1))
			{
				throw new ArgumentOutOfRangeException("validationLocalisation",
					"There should only be one validation localisation language for this method.");
			}

			var validationErrors = GetValidationErrors(entityRecord, validationLocalisation);

			if (validationErrors.Any())
			{
				var messages = validationErrors.Select(error => error.ErrorMessage.Values.First()).ToList();
				var id = ((dynamic)entityRecord).Id;
				messages.Insert(0, string.Format("Record of type \"{0}\"" + (id != null ? " and ID \"{1}\"" : "")
													+ " has invalid values.", entityRecord.GetType().Name, id));
				return messages;
			}

			return null;
		}

		/// <summary>
		///     Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///     Validation checks: required, numeric value range, and string length.<br />
		/// </summary>
		/// <param name="validationTypes">An array of validations to run.</param>
		/// <returns>A list of validation errors in the form of <see cref="ValidationError"/>.</returns>
		public static List<ValidationError> GetValidationErrors(this Entity entityRecord, ValidationType[] validationTypes)
		{
			return GetValidationErrors(entityRecord,
				validationLocationsDefaults.Where(local => validationTypes.Contains(local.ErrorType)).ToArray());
		}

		///  <summary>
		///      Validates that all field values in this record adhere to their contraints specified in CRM.<br />
		///      Validation checks: required, numeric value range, and string length.<br />
		///  </summary>
		///  <param name="validationLocalisation">
		///      [OPTIONAL] An array containing validation types with the message format to use for the validation error.<br />
		///      For validation type "Range", the message format takes three params for min, max, and actual value respectively;
		///          e.g.: "Value must be between {0} and {1}. Actual value was {2}.".<br />
		///      For validation type "MaxLength", the message format takes two params;
		/// 		    e.g.: "Value length must be less than {0}. Actual value was {1}."<br />
		///      Default is a check on all validation types.
		///  </param>
		/// <returns>A list of validation errors in the form of <see cref="ValidationError"/>.</returns>
		public static List<ValidationError> GetValidationErrors(this Entity entityRecord, ValidationLocalisation[] validationLocalisation = null)
		{
			var id = ((dynamic)entityRecord).Id;
			var fields = entityRecord.GetType().GetProperties();
			var exceptions = new List<ValidationError>();

			validationLocalisation = validationLocalisation ?? validationLocationsDefaults;

			var localisation = validationLocalisation.FirstOrDefault(local => local.ErrorType == ValidationType.Required);

			if (localisation != null)
			{
				exceptions.AddRange(from fieldQ in fields
									where Attribute.IsDefined(fieldQ, typeof(RequiredAttribute))
										  && fieldQ.GetValue(entityRecord) == null
									select new ValidationError
									{
										RecordId = id,
										AttributeName = fieldQ.Name,
										ErrorMessage =
												   localisation.Localisation.ToDictionary(local => local.Key, local => local.Value),
										ErrorType = ValidationType.Required
									});
			}

			localisation = validationLocalisation.FirstOrDefault(local => local.ErrorType == ValidationType.Range);

			if (localisation != null)
			{
				foreach (var field in fields.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(RangeAttribute)) && fieldQ.GetValue(entityRecord) != null))
				{
					var fieldValue = field.GetValue(entityRecord);
					Type type = field.GetCustomAttribute<RangeAttribute>().OperandType;

					var method = type.GetMethods().FirstOrDefault(methodQ => methodQ.GetParameters().Length == 1 && methodQ.Name == "Parse");

					var min = (IComparable)method.Invoke(null, new object[] { field.GetCustomAttribute<RangeAttribute>().Minimum });
					var max = (IComparable)method.Invoke(null, new object[] { field.GetCustomAttribute<RangeAttribute>().Maximum });

					if (fieldValue != null && (min.CompareTo(fieldValue) == 1 || max.CompareTo(fieldValue) == -1))
					{
						exceptions.Add(new ValidationError
						{
							RecordId = id,
							AttributeName = field.Name,
							ErrorMessage = localisation.Localisation.ToDictionary(local => local.Key,
												   local => string.Format(local.Value, min, max, fieldValue)),
							ErrorType = ValidationType.Range,
							NumericValue = decimal.Parse(fieldValue.ToString()),
							NumericRangeMin = decimal.Parse(min.ToString()),
							NumericRangeMax = decimal.Parse(max.ToString())
						});
					}
				}
			}


			localisation = validationLocalisation.FirstOrDefault(local => local.ErrorType == ValidationType.MaxLength);

			if (localisation != null)
			{
				exceptions.AddRange(from fieldQ in fields.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(MaxLengthAttribute)))
									let fieldValue = fieldQ.GetValue(entityRecord) as string
									let maxLength = fieldQ.GetCustomAttribute<MaxLengthAttribute>().Length
									where fieldValue != null && fieldValue.Length > maxLength
									select new ValidationError
									{
										RecordId = id,
										AttributeName = fieldQ.Name,
										ErrorMessage = localisation.Localisation.ToDictionary(local => local.Key,
													   local => string.Format(local.Value, maxLength, fieldValue)),
										ErrorType = ValidationType.MaxLength,
										StringValue = fieldValue,
										StringMaxLength = maxLength
									});
			}

			return exceptions;
		}

		public static TEntity ConvertTo<TEntity>(this GeneratedEntityBase crmRecord) where TEntity : EntityContract
		{
			// create entity object
			var record = Activator.CreateInstance<TEntity>();

			var entityType = typeof(TEntity);
			var entityLogicalName = entityType.GetCustomAttribute<CrmEntityMappingAttribute>().LogicalName;
			var properties = entityType.GetProperties().ToArray();

			// if logical names don't match
			if (entityLogicalName != crmRecord.LogicalName)
			{
				throw new Exception(string.Format("CRM entity '{0}' doesn't map to entity '{1}'.",
					crmRecord.GetType().Name, entityType.Name));
			}

			// go over all fields in the contract entity that has the mapping attribute
			foreach (var field in properties
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(CrmFieldMappingAttribute))
								 && !Attribute.IsDefined(fieldQ, typeof(CrmRelationMappingAttribute))))
			{
				// get the value of the mapping attribute
				var mapping = field.GetCustomAttribute<CrmFieldMappingAttribute>();
				SetValueInRecord(record, field, crmRecord, mapping);
			}

			var relations = properties
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(CrmRelationMappingAttribute))
					&& !Attribute.IsDefined(fieldQ, typeof(CrmFieldMappingAttribute))).ToArray();

			// go over all relations in the contract entity that has the mapping attribute
			foreach (var relation in relations)
			{
				// get the value of the mapping attribute
				var mapping = relation.GetCustomAttribute<CrmRelationMappingAttribute>();
				SetRelationInRecord(record, relation, crmRecord, mapping);
			}

			var flattableRelations = relations
				.Select(r =>
					new
					{
						r,
						p = properties
							.Where(p => Attribute.IsDefined(p, typeof(CrmFieldMappingAttribute))
								   && Attribute.IsDefined(p, typeof(CrmRelationMappingAttribute)))
							.Where(
								p =>
								{
									var rAttr = r.GetCustomAttribute<CrmRelationMappingAttribute>();
									var pAttr = p.GetCustomAttribute<CrmRelationMappingAttribute>();
									return rAttr.SchemaName == pAttr.SchemaName
										&& rAttr.Role == pAttr.Role;
								})
					})
				.Where(r => r.p.Any());

			// go over flattened relations in the contract entity that has the mapping attribute
			foreach (var r in flattableRelations)
			{
				SetRelationInRecord(record, r.r, r.p);
			}

			return record;
		}

		private static void SetValueInRecord<TEntity>(TEntity entity, PropertyInfo field, GeneratedEntityBase crmRecord,
			CrmFieldMappingAttribute mapping)
			where TEntity : EntityContract
		{
			// if key not found, then the CRM record doesn't have that value set
			var crmValue = crmRecord.GetAttributeValue<object>(mapping.LogicalName);

			if (crmValue == null)
			{
				return;
			}

			var crmField = crmRecord.GetType().GetProperties()
				.FirstOrDefault(propQ => Attribute.IsDefined(propQ, typeof(AttributeLogicalNameAttribute))
					&& propQ.GetCustomAttribute<AttributeLogicalNameAttribute>().LogicalName == mapping.LogicalName);

			if (crmField == null)
			{
				return;
			}

			var value = crmField.GetValue(crmRecord);

			if (value == null)
			{
				return;
			}

			// convert types
			if (value is Enum)
			{
				var underlyingType = Nullable.GetUnderlyingType(field.PropertyType);

				if (underlyingType == null)
				{
					return;
				}

				// can't set enum value with int using reflection!
				field.SetValue(entity, Enum.ToObject(underlyingType, Convert.ToInt32(value)));

				var labelsProperty = typeof(TEntity).GetProperties().FirstOrDefault(propQ => propQ.Name == field.Name + "Labels");

				if (labelsProperty != null)
				{
					// get the label value from the CRM record
					var matchingProperty = crmRecord.GetType().GetProperties().FirstOrDefault(propQ => propQ.Name == field.Name + "Labels");

					// if it has a value, set it in contract
					if (matchingProperty != null)
					{
						labelsProperty.SetValue(entity, matchingProperty.GetValue(crmRecord));
					}
				}
			}
			else if (crmValue is EntityReference)
			{
				var entityRef = (EntityReference)crmValue;

				if (field.PropertyType == typeof(Guid?))
				{
					field.SetValue(entity, entityRef.Id);
				}
				else if (field.PropertyType == typeof(LookupValue))
				{
					field.SetValue(entity, new LookupValue(entityRef.LogicalName, entityRef.Id));
				}

				var nameField = typeof(TEntity).GetProperties().FirstOrDefault(fieldQ => fieldQ.Name == field.Name + "Name");

				if (nameField != null && entityRef.Name != null)
				{
					nameField.SetValue(entity, entityRef.Name);
				}

				var labelsProperty = typeof(TEntity).GetProperties().FirstOrDefault(propQ => propQ.Name == field.Name + "Labels");

				if (labelsProperty != null)
				{
					// get the label value from the CRM record
					var matchingProperty = crmRecord.GetType().GetProperties().FirstOrDefault(propQ => Attribute.IsDefined(propQ, typeof(LabelAttribute))
						&& propQ.GetCustomAttribute<LabelAttribute>().FieldLogicalName == labelsProperty.GetCustomAttribute<LabelAttribute>().FieldLogicalName);

					// if it has a value, set it in contract
					if (matchingProperty != null)
					{
						labelsProperty.SetValue(entity, matchingProperty.GetValue(crmRecord));
					}
				}
			}
			else
			{
				field.SetValue(entity, value);
			}
		}

		private static void SetRelationInRecord<TEntity>(TEntity record, PropertyInfo field, Entity crmRecord,
			CrmRelationMappingAttribute mapping)
			where TEntity : EntityContract
		{
			var crmRelation = crmRecord.GetType().GetProperties()
				.FirstOrDefault(propQ => Attribute.IsDefined(propQ, typeof(RelationshipSchemaNameAttribute))
					&& propQ.GetCustomAttribute<RelationshipSchemaNameAttribute>().SchemaName == mapping.SchemaName
					&& propQ.GetCustomAttribute<RelationshipSchemaNameAttribute>().PrimaryEntityRole.ToString() == mapping.Role.ToString());

			if (crmRelation == null)
			{
				return;
			}

			var fieldType = field.PropertyType;

			object convertedValue = null;

			// x-N relation
			if (typeof(Array).IsAssignableFrom(fieldType))
			{
				var value = crmRelation.GetValue(crmRecord) as Array;

				if (value == null || value.Length <= 0)
				{
					return;
				}

				// get the contract type for the relation
				var elementType = fieldType.GetElementType();

				if (elementType == null)
				{
					return;
				}

				// create an array for the related entities of the appropriate type
				var relatedRecords = Array.CreateInstance(elementType, value.Length);

				// create the method reference that will be used to convert the related entities to the CRM entities
				var method = MethodBase.GetCurrentMethod().DeclaringType?
					.GetMethod("ConvertTo")?.MakeGenericMethod(elementType);

				if (method == null)
				{
					return;
				}

				// convert all entities to contract entities
				var objectRelatedRecords =
					(from object relatedRecord in value
					 select method.Invoke(relatedRecord, new[] { relatedRecord })).ToArray();

				// copy the object entities to the array of the specific type
				Array.Copy(objectRelatedRecords, relatedRecords, relatedRecords.Length);
				convertedValue = relatedRecords;
			}
			else if (typeof(EntityContract).IsAssignableFrom(fieldType))
			{
				var value = crmRelation.GetValue(crmRecord) as GeneratedEntityBase;

				if (value == null)
				{
					return;
				}

				// create the method reference that will be used to convert the related entities to the CRM entities
				var method = MethodBase.GetCurrentMethod().DeclaringType?
					.GetMethod("ConvertTo")?.MakeGenericMethod(fieldType);

				if (method == null)
				{
					return;
				}

				convertedValue = method.Invoke(value, new object[] { value });
			}

			// set the related entities value in the entity
			field.SetValue(record, convertedValue);
		}

		private static void SetRelationInRecord(object record, PropertyInfo relationInfo, IEnumerable<PropertyInfo> flatPropertiesP)
		{
			var relation = relationInfo.GetValue(record);

			if (relation == null)
			{
				return;
			}

			var flatProperties = flatPropertiesP.ToArray();

			var relationProperties = relationInfo.PropertyType.GetProperties()
				.Where(p => Attribute.IsDefined(p, typeof(CrmFieldMappingAttribute))
					&& !Attribute.IsDefined(p, typeof(CrmRelationMappingAttribute)));

			foreach (var relationProperty in relationProperties)
			{
				var flatProperty = flatProperties
					.FirstOrDefault(f => f.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName
						== relationProperty.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName);

				if (flatProperty == null)
				{
					continue;
				}

				flatProperty.SetValue(record, relationProperty.GetValue(relation));
			}

			relationInfo.SetValue(record, null);
		}

		/// <summary>
		/// Returns an array of logical names whose property is marked for WCF contract.
		/// </summary>
		/// <param name="entity"></param>
		/// <returns>An array of logical names.</returns>
		public static string[] GetDataMemberAttributes(this Entity entity)
		{
			return entity.GetType().GetProperties()
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(DataMemberAttribute)) && Attribute.IsDefined(fieldQ, typeof(AttributeLogicalNameAttribute)))
				.Select(field => field.GetCustomAttribute<AttributeLogicalNameAttribute>().LogicalName).ToArray();
		}

		/// <summary>
		/// Returns an array of logical names of the properties.
		/// </summary>
		/// <param name="entity"></param>
		/// <returns>An array of logical names.</returns>
		public static string[] GetAttributeNames(this Entity entity)
		{
			return entity.GetType().GetProperties()
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(AttributeLogicalNameAttribute)) && !Attribute.IsDefined(fieldQ, typeof(RelationshipSchemaNameAttribute)))
				.Select(field => field.GetCustomAttribute<AttributeLogicalNameAttribute>().LogicalName).ToArray();
		}

		/// <summary>
		/// Loads the values of data member properties from CRM.
		/// </summary>
		/// <param name="entity"></param>
		/// <param name="service">CRM organisation service.</param>
		public static void LoadDataMemberAttributes(this Entity entity, IOrganizationService service)
		{
			LoadAttributeValues(entity, service, entity.GetDataMemberAttributes());
		}

		/// <summary>
		/// Loads the values of all properties from CRM.
		/// </summary>
		/// <param name="entity"></param>
		/// <param name="service">CRM organisation service.</param>
		/// <param name="attributes"></param>
		public static void LoadAttributeValues(this Entity entity, IOrganizationService service, params string[] attributes)
		{
			try
			{
				entity.Id = entity.Id;
			}
			catch
			{
				throw new Exception("Entity is ready only. Set 'MergeOption' to 'NoTracking' in the context used for fetching this entity.");
			}

			var isLoadAll = attributes == null || attributes.Length <= 0;

			if (isLoadAll)
			{
				entity.Attributes.Clear();
			}

			foreach (var attribute in service.Retrieve(entity.LogicalName, entity.Id, new ColumnSet(isLoadAll ? entity.GetAttributeNames() : attributes)).Attributes)
			{
				entity[attribute.Key] = attribute.Value;
			}
		}
	}

	#endregion

	#region Helpers

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public abstract class GeneratedEntityBase : Entity
	{
		protected GeneratedEntityBase()
		{ }

		protected GeneratedEntityBase(string logicalName) : base(logicalName)
		{ }

		/// <summary>
		///     Initialises this entity with the given keys and values.
		/// </summary>
		protected GeneratedEntityBase(string[] keys, object[] values, string logicalName) : this(logicalName)
		{
			for (var i = 0; i < keys.Length; i++)
			{
				var key = keys[i];
				var value = values[i];

				if (value == null)
				{
					continue;
				}

				Attributes[key] = value;
			}
		}

		/// <summary>
		/// Constructor for populating via LINQ queries given a LINQ anonymous type.<br />
		/// <b>OR</b> ...<br />
		///     Constructor that Limits attributes retrieved from CRM on any early-bound entity in a LINQ query.<br />
		///     The properties and fields defined in this class will be the ones retrieved from CRM by default; all other attributes
		///     in CRM will be ignored.<br />
		///     The selected early-bound record of this class's type in the query <b>must</b> be passed to this constructor.<br />
		///     Example:
		///     <code>
		/// (from account in new XrmServiceContext(service).AccountSet
		/// select new Account(account)).ToList()
		/// </code>
		/// </summary>
		protected GeneratedEntityBase(object obj, string logicalName) : base(logicalName)
		{ }

		/// <summary>
		///     Limits attributes retrieved from CRM on any early-bound entity in a LINQ.<br />
		///     The properties and fields defined in the 'limitingType' class will be the ones retrieved from CRM by default;
		///     all other attributes in CRM will be ignored.<br />
		///     The selected early-bound record of this class's type in the query <b>must</b> be passed to this constructor.<br />
		///     Example:
		///     <code>
		/// (from account in new XrmServiceContext(service).AccountSet
		/// select new Account(account, typeof(AccountModel))).ToList()
		/// </code>
		/// </summary>
		protected GeneratedEntityBase(object obj, Type limitingType, string logicalName) : base(logicalName)
		{ }

		[DataMember]
		private List<string> NullValuedAttributes { get; set; }
		internal IList<QueryAction> DeferredQueriesList = new List<QueryAction>();
		protected IDictionary<string, object[]> relationProperties;
		public virtual IDictionary<string, object[]> RelationProperties { get { return new Dictionary<string, object[]>(); } }

		#region Serialisation events

		[OnDeserialized]
		private void DeserializedInitializer(StreamingContext ctx)
		{
			LogicalName = (string)GetType().GetField("EntityLogicalName").GetRawConstantValue();
			Attributes.Where(attribute => attribute.Value == null).ToList().ForEach(attribute => Attributes.Remove(attribute.Key));
			if (NullValuedAttributes != null && NullValuedAttributes.Count > 0)
			{
				NullValuedAttributes.ForEach(attribute =>
				{
					var property = GetType().GetProperty(attribute);
					if (property == null)
					{
						throw new Exception("Couldn't find the property '" + attribute + "' in entity.");
					}
					property.SetValue(this, null);
				});
			}
		}

		#endregion

		public XrmServiceContext ServiceContext { get; set; }

		public void LoadLookupLabels(IOrganizationService service, bool isDeferred = false)
		{
			var properties = from propQ in this.GetType().GetProperties()
							 let propIdQ = this.GetType().GetProperty(propQ.Name.Replace("Labels", ""))
							 where Attribute.IsDefined(propQ, typeof(LabelAttribute))
								   && (Guid?)propIdQ.GetValue(this) != null
							 select new
							 {
								 property = propQ,
								 id = propIdQ.GetValue(this),
								 attribute = propQ.GetCustomAttribute<LabelAttribute>()
							 };

			foreach (var property in properties)
			{
				var fields = property.attribute.LabelFieldNames.Split(',');

				var query = new QueryExpression(property.attribute.LogicalName);
				query.Criteria.AddCondition(property.attribute.IdFieldName, ConditionOperator.Equal, property.id);
				query.ColumnSet = new ColumnSet(fields.Select(field => field.Substring(5)).ToArray());

				var queryAction =
					new QueryAction(query)
					{
						Action =
							result =>
							{
								var response = result as RetrieveMultipleResponse;
								if (response == null) return;

								var entityQ = response.EntityCollection.Entities.FirstOrDefault();
								if (entityQ == null) return;

								var englishLabel = (string)entityQ.Attributes.FirstOrDefault(
									attribute => ("1033_" + attribute.Key) == fields.FirstOrDefault(field => field.Contains("1033_"))).Value;
								var dictionary = new Dictionary<int, string>();
								dictionary[1033] = englishLabel;
								var langLabel = (string)entityQ.Attributes.FirstOrDefault(attribute =>
									   ("3082_" + attribute.Key) == fields.FirstOrDefault(field => field.StartsWith("3082_"))).Value
												?? englishLabel;
								dictionary[3082] = langLabel;
								property.property.SetValue(this, dictionary);
							}
					};

				if (isDeferred)
				{
					this.DeferredQueriesList.Add(queryAction);
				}
				else
				{
					queryAction.Action.Invoke(service.Execute(new RetrieveMultipleRequest { Query = query }));
				}
			}
		}

		#region Relationship methods

		protected override IEnumerable<TEntity> GetRelatedEntities<TEntity>(string relationshipSchemaName, EntityRole? primaryEntityRole)
		{
			var key = new Relationship(relationshipSchemaName) { PrimaryEntityRole = primaryEntityRole };

			var enumerable = base.GetRelatedEntities<TEntity>(relationshipSchemaName, primaryEntityRole);

			if (ServiceContext != null && enumerable == null)
			{
				if (!ServiceContext.IsAttached(this))
				{
					throw new Exception("The context that loaded this entity must be used to load relationships, " +
										"or set 'MergeOption' to anything other than 'NoTracking' in the context before fetching this entity using LINQ.");
				}

				ServiceContext.LoadProperty(this, key);
			}

			enumerable = base.GetRelatedEntities<TEntity>(relationshipSchemaName, primaryEntityRole);

			return enumerable;
		}

		protected override TEntity GetRelatedEntity<TEntity>(string relationshipSchemaName, EntityRole? primaryEntityRole)
		{
			var key = new Relationship(relationshipSchemaName) { PrimaryEntityRole = primaryEntityRole };

			var result = base.GetRelatedEntity<TEntity>(relationshipSchemaName, primaryEntityRole);

			if (ServiceContext != null && result == null)
			{
				if (!ServiceContext.IsAttached(this))
				{
					throw new Exception("The context that loaded this entity must be used to load relationships, " +
										"or set 'MergeOption' to anything other than 'NoTracking' in the context before fetching this entity using LINQ.");
				}

				ServiceContext.LoadProperty(this, key);
			}

			result = base.GetRelatedEntity<TEntity>(relationshipSchemaName, primaryEntityRole);

			return result;
		}

		#endregion
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public abstract class GeneratedEntity<TRelationName> : GeneratedEntityBase where TRelationName : RelationNameBase
	{
		protected GeneratedEntity(string logicalName) : base(logicalName)
		{ }

		/// <inheritdoc/>
		protected GeneratedEntity(string[] keys, object[] values, string logicalName) : base(keys, values, logicalName)
		{ }

		/// <inheritdoc/>
		protected GeneratedEntity(object obj, Type limitingType, string logicalName) : base(obj, limitingType, logicalName)
		{ }

		/// <inheritdoc/>
		protected GeneratedEntity(object obj, string logicalName) : base(obj, logicalName)
		{ }

		#region Relationship methods

		/// <summary>
		/// Fetch the records related to this entity on this relationship. 
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, params string[] attributes)
		{
			return LoadRelation(relationName, service, false, -1, -1, null, null, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship. 
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, -1, -1, null, null, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship.
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, FilterExpression filter, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, -1, -1, null, filter, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship. The record limit accepts '-1', which means 'unlimited'.
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, int recordCountLimit, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, recordCountLimit, -1, null, null, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship. The record limit accepts '-1', which means 'unlimited'.
		/// The page param accepts '-1', which means 'all pages'. If a page is specified, the record limit won't exceed '5000' internally.
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, int recordCountLimit, int page, string cookie, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, recordCountLimit, page, cookie, null, attributes);
		}

		/// <summary>
		/// Fetch the next page of records related to this entity on this relationship using the previous paging info object returned.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, RelationPagingInfo pagingInfo, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, pagingInfo.RecordCountLimit, pagingInfo.NextPage, pagingInfo.Cookie, pagingInfo.Filter, pagingInfo, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship. The record limit accepts '-1', which means 'unlimited'.
		/// The page param accepts '-1', which means 'all pages'. If a page is specified, the record limit won't exceed '5000' internally.
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, int recordCountLimit, int page, string cookie, FilterExpression filter, params string[] attributes)
		{
			return LoadRelation(relationName, service, isDeferred, recordCountLimit, page, cookie, filter, null, attributes);
		}

		/// <summary>
		/// Fetch the records related to this entity on this relationship. The record limit accepts '-1', which means 'unlimited'.
		/// The page param accepts '-1', which means 'all pages'. If a page is specified, the record limit won't exceed '5000' internally.
		/// To specify columns to fetch, the "attributes" param accepts either "*", which means all attributes; empty, which means no attributes;
		/// or a list of column names to fetch. If 'deferred', then loading will be added to the queue to be executed later upon request.
		/// </summary>
		public RelationPagingInfo LoadRelation(TRelationName relationName, IOrganizationService service, bool isDeferred, int recordCountLimit, int page, string cookie, FilterExpression filter, RelationPagingInfo pagingInfo, params string[] attributes)
		{
			if (RelatedEntities.IsReadOnly)
			{
				throw new Exception("Relationship collection is ready only. The context that loaded this entity from CRM must be passed as a parameter, " +
									"or set 'MergeOption' to 'NoTracking' in the context before fetching this entity using LINQ.");
			}
			if (!RelationProperties.ContainsKey(relationName.Name))
			{
				throw new Exception("Relation does not exist in entity, or is not generated.");
			}
			var relationPagingInfo = pagingInfo ?? new RelationPagingInfo
			{
				RecordCountLimit = recordCountLimit,
				Filter = filter,
				Cookie = cookie,
				NextPage = page
			};
			var relationParams = RelationProperties[relationName.Name];
			var queryActionObject = new QueryAction(GeneratorHelpers.GetLoadRelationQuery(this, service, (string)relationParams[1], (string)relationParams[2], (string)relationParams[3], (string)relationParams[4], (string)relationParams[5], (string)relationParams[6],
					recordCountLimit, page, cookie, filter, attributes));
			var queryAction = queryActionObject.Action =
							  resultQ => {
								  var response = resultQ as RetrieveMultipleResponse;
								  var entityType = ((Type)relationParams[8]).GetElementType() ?? (Type)relationParams[8];
								  var resultArray = response == null ? ((List<Entity>)resultQ).Select(entityQ => entityQ.GetType().GetMethod("ToEntity").MakeGenericMethod(entityType).Invoke(entityQ, null)).ToArray()
									  : response.EntityCollection.Entities.Select(entityQ => entityQ.GetType().GetMethod("ToEntity").MakeGenericMethod(entityType).Invoke(entityQ, null)).ToArray();
								  var relatedRecords = Array.CreateInstance(entityType, resultArray.Length);
								  Array.Copy(resultArray, relatedRecords, resultArray.Length);
								  DeferredQueriesList.Remove(queryActionObject);
								  var newValue = relatedRecords.Length <= 0 ? null : (((Type)relationParams[8]).GetElementType() == null ? relatedRecords.GetValue(0) : relatedRecords);
								  GetType().GetProperty((string)relationParams[0]).SetValue(this, newValue);
							  };
			if (isDeferred) DeferredQueriesList.Add(queryActionObject);
			else queryAction.Invoke(GeneratorHelpers.LoadRelation(service, queryActionObject.Query, recordCountLimit, page, cookie, relationPagingInfo));
			return relationPagingInfo;
		}

		#endregion
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class RelationPagingInfo
	{
		public string Cookie;
		public int NextPage = 1;
		public int RecordCountLimit = -1;
		public FilterExpression Filter;
		public bool IsMoreRecords = true;
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public abstract class RelationNameBase
	{
		public string Name;

		public RelationNameBase(string name)
		{
			Name = name;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	internal class QueryAction
	{
		public QueryExpression Query { get; set; }
		public Action<object> Action { get; set; }

		public QueryAction(QueryExpression query, Action<object> action = null)
		{
			Query = query;
			Action = action;
		}
	}

	public enum ValidationType
	{
		Required,
		Range,
		MaxLength
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public abstract class LookupKeysBase
	{
		public string Name;

		protected LookupKeysBase(string name)
		{
			Name = name;
		}
	}

	public interface ILookupKeys<in TKey> where TKey : LookupKeysBase
	{
		void AddKey(TKey key, object value);
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract]
	public class CrmActionBase<TInputs, TOutputs>
		where TInputs : CrmActionBase<TInputs, TOutputs>.InputsBase, new()
		where TOutputs : CrmActionBase<TInputs, TOutputs>.OutputsBase, new()
	{
		public IOrganizationService Service;
		public OrganizationRequest Request;
		public OrganizationResponse Response;
		public TInputs InputParams;
		public TOutputs OutputFields;

		public CrmActionBase(string actionName)
		{
			Request = new OrganizationRequest(actionName);
			InputParams = new TInputs() { Request = Request };
		}

		public CrmActionBase(IOrganizationService service, string actionName) : this(actionName)
		{
			Service = service;
		}

		public TOutputs Execute(IOrganizationService service = null)
		{
			if (service != null)
			{
				Service = service;
			}

			Response = Service.Execute(Request);

			return OutputFields = new TOutputs() { Response = Response };
		}

		public abstract class InputsBase
		{
			public OrganizationRequest Request;

			public InputsBase()
			{ }

			public InputsBase(OrganizationRequest request)
			{
				Request = request;
			}
		}

		public abstract class OutputsBase
		{
			public OrganizationResponse Response;

			public OutputsBase()
			{ }

			public OutputsBase(OrganizationResponse response)
			{
				Response = response;
			}
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public static partial class GeneratorHelpers
	{
		#region Enums

		/// <summary>
		/// Get the value that corresponds to the label from the option-set,
		/// using the type of the class enclosing both, label type, and the language code given.
		/// </summary>
		/// <param name="labelType">The type of the class containing the labels; e.g.: 'typeof(Account.Enums.Labels.IndustryCode)'</param>
		/// <param name="label">The label to search for, corresponding to the value</param>
		/// <param name="languageCode">The language code from CRM</param>
		/// <returns>The value corresponding to the label</returns>
		public static int GetValue(Type labelType, string label, int languageCode = 1033)
		{
			var labelsType = labelType.DeclaringType;

			if (labelsType == null)
			{
				return -1;
			}

			var enumsType = labelsType.DeclaringType;

			if (enumsType == null)
			{
				return -1;
			}

			// get the fields with the same label from the label class
			var fields = labelType.GetFields()
				.Where(fieldQ => fieldQ.Name.Contains(languageCode.ToString())
					&& (string)fieldQ.GetValue(labelType) == label);

			if (!fields.Any())
			{
				return -1;
			}

			var field = fields.First();

			var entityType = enumsType.DeclaringType;

			if (entityType == null)
			{
				return -1;
			}

			var enumType = entityType.GetNestedType(labelType.Name + "Enum");

			if (enumType == null)
			{
				return -1;
			}

			// get the enum constant corresponding to the field name
			var enumConstant = Enum.Parse(enumType, field.Name.Replace("_" + languageCode, ""));

			return (int)enumConstant;
		}

		/// <summary>
		/// Gets the value corresponding to the option-set's label using its logical name,
		/// the value within, and the language code.
		/// </summary>
		/// <param name="logicalName">The logical name of the option-set in CRM</param>
		/// <param name="label">The label from the option-set</param>
		/// <param name="enumsType">The 'Enums' type; e.g.: 'typeof(Account.Enums)'</param>
		/// <param name="languageCode">The language code from CRM</param>
		/// <returns>The value corresponding to the label</returns>
		public static int GetValue(string logicalName, string label, Type enumsType, int languageCode = 1033)
		{
			var labelType = GetLabelType(enumsType, logicalName);

			return GetValue(labelType, label, languageCode);
		}

		private static Type GetEnumType(Type enumsType, string logicalName)
		{
			var field = GetLogicalNameField(enumsType, logicalName);

			var entityType = enumsType.DeclaringType;

			if (entityType == null)
			{
				throw new Exception("Can't find the entity type from the enum type.");
			}

			return field == null ? null : entityType.GetNestedType(field.Name + "Enum");
		}

		#endregion

		#region Labels

		/// <summary>
		/// Gets the label corresponding to the option-set's value using its logical name,
		/// the value within, and the language code.
		/// </summary>
		/// <param name="logicalName">The logical name of the option-set in CRM</param>
		/// <param name="constant">The value from the option-set</param>
		/// <param name="enumsType">The 'Enums' type; e.g.: 'typeof(Account.Enums)'</param>
		/// <param name="languageCode">The language code from CRM</param>
		/// <returns></returns>
		public static string GetLabel(string logicalName, int constant, Type enumsType, int languageCode = 1033)
		{
			var enumType = GetEnumType(enumsType, logicalName);

			if (enumType == null)
			{
				return null;
			}

			var enumName = enumType.Name;
			var constantName = enumType.GetEnumName(constant);
			var labelsType = enumsType.GetNestedType("Labels");

			if (labelsType == null)
			{
				return null;
			}

			var labelType = labelsType.GetNestedType(enumName.Substring(0, enumType.Name.LastIndexOf("Enum")));

			if (labelType == null)
			{
				return null;
			}

			var field = labelType.GetField(constantName + "_" + languageCode);

			return field == null ? null : field.GetValue(labelType).ToString();
		}

		private static Type GetLabelType(Type enumsType, string logicalName)
		{
			var field = GetLogicalNameField(enumsType, logicalName);
			return enumsType.GetNestedType("Labels").GetNestedType(field.Name);
		}

		#endregion

		private static FieldInfo GetLogicalNameField(Type enumsType, string logicalName)
		{
			var namesType = enumsType.GetNestedType("Names");
			return namesType.GetFields().FirstOrDefault(fieldQ => (string)fieldQ.GetValue(namesType) == logicalName);
		}

		internal static QueryExpression GetLoadRelationQuery(Entity entity, IOrganizationService service,
			string fromEntityName, string toEntityName, string fromFieldName, string toFieldName,
			string idFieldName, string intersectIdFieldName, int limit = -1, int page = -1, string cookie = null,
			FilterExpression filter = null, params string[] attributes)
		{
			limit = limit <= 0 ? int.MaxValue : limit;

			// create the query taking into account paging
			var query = new QueryExpression(fromEntityName);
			query.LinkEntities.Add(new LinkEntity(fromEntityName, toEntityName, fromFieldName, toFieldName, JoinOperator.Inner));
			query.LinkEntities[0].EntityAlias = "linkedEntityAlias";
			query.Criteria.AddCondition("linkedEntityAlias", intersectIdFieldName, ConditionOperator.Equal, entity[idFieldName]);

			if (filter != null)
			{
				query.Criteria.AddFilter(filter);
			}

			if (attributes.Length == 1 && attributes[0] == "*")
			{
				query.ColumnSet = new ColumnSet(true);
			}
			else if (attributes.Length > 0)
			{
				query.ColumnSet = new ColumnSet(attributes);
			}
			else
			{
				query.ColumnSet = new ColumnSet(false);
			}

			query.PageInfo = new PagingInfo
			{
				PageNumber = page <= 0 ? 1 : page,
				Count = limit,
				PagingCookie = cookie
			};

			return query;
		}

		internal static List<Entity> LoadRelation(Entity entity, IOrganizationService service,
			string fromEntityName, string toEntityName, string fromFieldName, string toFieldName,
			string idFieldName, string intersectIdFieldName, int limit = -1, int page = -1,
			FilterExpression filter = null, string cookie = null, RelationPagingInfo relationPagingInfo = null, params string[] attributes)
		{
			return LoadRelation(service, GetLoadRelationQuery(entity, service, fromEntityName, toEntityName,
				fromFieldName, toFieldName, idFieldName, intersectIdFieldName, limit, page, cookie,
				filter, attributes), limit, page, cookie, relationPagingInfo);
		}

		public static List<Entity> LoadRelation(IOrganizationService service, QueryExpression query,
			int limit = -1, int page = -1, string cookie = null, RelationPagingInfo relationPagingInfo = null)
		{
			limit = limit <= 0 ? int.MaxValue : limit;
			query.PageInfo = query.PageInfo ??
				new PagingInfo
				{
					PageNumber = page <= 0 ? 1 : page,
					Count = limit
				};
			query.PageInfo.PagingCookie = cookie ?? relationPagingInfo.Cookie ?? query.PageInfo.PagingCookie;

			EntityCollection records;
			var entities = new List<Entity>();

			// get all records
			do
			{
				// fetch the records
				records = service.RetrieveMultiple(query);

				// next time get the next bundle of records
				query.PageInfo.PagingCookie = records.PagingCookie;
				query.PageInfo.PageNumber++;

				// add to existing list
				entities.AddRange(records.Entities);
			} while (records.MoreRecords && entities.Count < limit && page <= 0);

			if (relationPagingInfo != null)
			{
				relationPagingInfo.Cookie = query.PageInfo.PagingCookie;
				relationPagingInfo.NextPage = query.PageInfo.PageNumber;
				relationPagingInfo.IsMoreRecords = records.MoreRecords;
			}

			return entities.ToList();
		}

		/// <summary>
		/// Executes the queries in the query queue in each entity passed, and executes the action related to the query after.<br />
		/// Returns a list of errors per entity processed.
		/// </summary>
		/// <param name="service">CRM service to use to execute query.</param>
		/// <param name="entities">List of entities containing the queues.</param>
		public static IDictionary<GeneratedEntityBase, IList<string>> ProcessDeferredQueries(IOrganizationService service,
			params GeneratedEntityBase[] entities)
		{
			return ProcessDeferredQueries(service, 100, entities);
		}

		/// <summary>
		/// Executes the queries in the query queue in each entity passed, and executes the action related to the query after.<br />
		/// Returns a list of errors per entity processed.
		/// </summary>
		/// <param name="service">CRM service to use to execute query.</param>
		/// <param name="bulkSize">The number of requests from the queue to execute in each iteration.</param>
		/// <param name="entities">List of entities containing the queues.</param>
		internal static IDictionary<GeneratedEntityBase, IList<string>> ProcessDeferredQueries(IOrganizationService service,
			int bulkSize, params GeneratedEntityBase[] entities)
		{
			var errorList = new Dictionary<GeneratedEntityBase, IList<string>>();

			// exit if no entities to process
			if (!entities.Any()) return errorList;

			bulkSize = Math.Min(1000, bulkSize);

			// filter entities to only the ones with a queue
			entities = entities.Where(entity => entity.DeferredQueriesList.Any()).ToArray();

			// queue to assign errors to proper entity and to find the original query and invoke the action
			var queryActionQueue = new Queue<KeyValuePair<GeneratedEntityBase, QueryAction>>();

			// create a queue to support paging in bulk execution
			var requestsQueue = new Queue<OrganizationRequest>();

			// go over the entities and fill the queues
			foreach (var entity in entities)
			{
				foreach (var queryAction in entity.DeferredQueriesList)
				{
					queryActionQueue.Enqueue(new KeyValuePair<GeneratedEntityBase, QueryAction>(entity, queryAction));
					requestsQueue.Enqueue(new RetrieveMultipleRequest { Query = queryAction.Query });
				}
			}

			var bulkQuery = new ExecuteMultipleRequest
			{
				Settings = new ExecuteMultipleSettings
				{
					ContinueOnError = true,
					ReturnResponses = true
				},
				Requests = new OrganizationRequestCollection()
			};

			while (requestsQueue.Any())
			{
				bulkQuery.Requests.Clear();

				// page execution
				do
				{
					bulkQuery.Requests.Add(requestsQueue.Dequeue());
				} while (bulkQuery.Requests.Count % bulkSize != 0 && requestsQueue.Any());

				var result = (ExecuteMultipleResponse)service.Execute(bulkQuery);

				foreach (var response in result.Responses)
				{
					var queryAction = queryActionQueue.Dequeue();
					var entity = queryAction.Key;

					// parse fault
					if (response.Fault != null)
					{
						if (!errorList.ContainsKey(entity))
						{
							errorList.Add(entity, new List<string>());
						}

						errorList[entity].Add(string.Format("Error code: {0}.\nError message: {1}.",
							response.Fault.ErrorCode, response.Fault.Message)
													 + (!string.IsNullOrEmpty(response.Fault.TraceText)
															? "\nError trace: " + response.Fault.TraceText
															: ""));

						continue;
					}

					queryAction.Value.Action.Invoke(response.Response);
				}
			}

			return errorList;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class EntityComparer : IEqualityComparer<Entity>
	{
		public bool Equals(Entity x, Entity y)
		{
			return x.Id == y.Id;
		}

		public int GetHashCode(Entity obj)
		{
			return obj.Id.GetHashCode();
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public static class TypeHelpers
	{
		public static Type GetType(string name, Type assemblyScope = null)
		{
			return assemblyScope == null
				? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
					.FirstOrDefault(e => e.AssemblyQualifiedName == name || e.FullName == name || e.Name == name)
				: assemblyScope.Assembly.GetTypes()
					.FirstOrDefault(e => e.AssemblyQualifiedName == name || e.FullName == name || e.Name == name);
		}
	}

	public partial class EntityContract
	{
		public TCrmEntity ConvertTo<TCrmEntity>(ClearMode? clearMode = null) where TCrmEntity : Entity
		{
			// create CRM entity object
			var crmRecord = Activator.CreateInstance<TCrmEntity>();

			var entityType = GetType();
			var entityLogicalName = entityType.GetCustomAttribute<CrmEntityMappingAttribute>().LogicalName;

			// if logical names don't match
			if (entityLogicalName != crmRecord.LogicalName)
			{
				throw new Exception($"Entity '{entityType.Name}' doesn't map to CRM entity '{typeof(TCrmEntity).Name}'.");
			}

			clearMode = clearMode
				?? (ClearMode?)entityType.GetProperty("ValueClearMode")?.GetValue(this)
					?? ClearMode.Disabled;

			// go over all fields in the contract entity that has the mapping attribute
			foreach (var field in entityType.GetProperties()
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(CrmFieldMappingAttribute))
					&& !Attribute.IsDefined(fieldQ, typeof(CrmRelationMappingAttribute))))
			{
				// get the value of the mapping attribute
				var mapping = field.GetCustomAttribute<CrmFieldMappingAttribute>();
				// get the value of the field
				var value = field.GetValue(this);
				// is the field value read only
				var isReadOnly = field.GetCustomAttribute<ReadOnlyFieldAttribute>() != null;
				// get the clear flag mode value if it exists
				var isClearFlag = clearMode == ClearMode.Flag
					&& (bool?)entityType.GetProperties().Where(fieldQ => fieldQ.Name == "Clear_" + field.Name)
						.Select(fieldQ => fieldQ.GetValue(this)).FirstOrDefault() == true;
				// check 'empty' mode
				var isClearEmpty = clearMode == ClearMode.Empty;
				var isClearConvention = clearMode == ClearMode.Convention;

				var isSetValue = value != null || isClearFlag || isClearEmpty || isClearConvention;

				// skip if no value and clear mode does not match and pass check
				if (isReadOnly || !isSetValue)
				{
					continue;
				}

				SetValueInCrmRecord(value, crmRecord, field.Name, mapping, clearMode, isClearFlag);
			}

			// go over flattened relations in the contract entity that has the mapping attribute
			foreach (var field in GetType().GetProperties()
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(CrmRelationMappingAttribute))
					&& Attribute.IsDefined(fieldQ, typeof(CrmFieldMappingAttribute))))
			{
				// get the value of the field
				var value = field.GetValue(this);
				// is the field value read only
				var isReadOnly = field.GetCustomAttribute<ReadOnlyFieldAttribute>();

				// skip if no value
				if (isReadOnly != null || (value == null && clearMode != ClearMode.Empty) || value is Array)
				{
					continue;
				}

				SetRelationInRecord(value, field);
			}

			// go over all relations in the contract entity that has the mapping attribute
			foreach (var relation in GetType().GetProperties()
				.Where(fieldQ => Attribute.IsDefined(fieldQ, typeof(CrmRelationMappingAttribute))
					&& !Attribute.IsDefined(fieldQ, typeof(CrmFieldMappingAttribute))))
			{
				// get the value of the mapping attribute
				var mapping = relation.GetCustomAttribute<CrmRelationMappingAttribute>();
				// get the value of the field
				var value = relation.GetValue(this);
				// is the relation read only
				var isReadOnly = relation.GetCustomAttribute<ReadOnlyFieldAttribute>();

				// skip if no value
				if (isReadOnly != null || value == null || (value is Array && ((Array)value).Length <= 0))
				{
					continue;
				}

				SetRelationInCrmRecord(value, crmRecord, mapping, clearMode);
			}

			return crmRecord;
		}

		private void SetRelationInRecord(object value, PropertyInfo propertyInfo)
		{
			// get the property representing the relationship
			var relation = GetType().GetProperties()
				.Where(fQ => !Attribute.IsDefined(fQ, typeof(CrmFieldMappingAttribute))
					&& Attribute.IsDefined(fQ, typeof(CrmRelationMappingAttribute)))
				.FirstOrDefault(
					fQ =>
					{
						var rAttr = fQ.GetCustomAttribute<CrmRelationMappingAttribute>();
						var relationAttr = propertyInfo.GetCustomAttribute<CrmRelationMappingAttribute>();
						return rAttr.SchemaName == relationAttr.SchemaName && rAttr.Role == relationAttr.Role;
					});

			if (relation == null)
			{
				return;
			}

			// get the related entity type
			var type = relation.PropertyType;
			// get the target property in the related entity to set the value
			var mappedProperty = type.GetProperties()
				.Where(fQ => Attribute.IsDefined(fQ, typeof(CrmFieldMappingAttribute))
					&& !Attribute.IsDefined(fQ, typeof(CrmRelationMappingAttribute)))
				.FirstOrDefault(p => p.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName
					== propertyInfo.GetCustomAttribute<CrmFieldMappingAttribute>().LogicalName);

			if (mappedProperty == null)
			{
				return;
			}

			// get the relation value
			var record = relation.GetValue(this);

			if (record == null)
			{
				// create a new instance of the related entity if it's null
				record = Activator.CreateInstance(type);
				// save the relation object in this entity
				relation.SetValue(this, record);
			}

			// set the field value in the related record
			mappedProperty.SetValue(record, value);
		}

		private static void SetValueInCrmRecord<TCrmEntity>(object value, TCrmEntity crmRecord,
			string fieldName, CrmFieldMappingAttribute mapping, ClearMode? clearMode = null, bool isClearFlag = false)
			where TCrmEntity : Entity
		{
			var crmProperty = GetCrmProperty<TCrmEntity>(mapping);

			if (crmProperty == null)
			{
				return;
			}

			// if no value, and clear mode is global or flagged, then clear
			if (value == null && (clearMode == ClearMode.Empty || isClearFlag))
			{
				crmRecord[mapping.LogicalName] = null;
			}

			// convert types
			if (value is Enum)
			{
				// if clear mode is convention, and value fits convention, then clear
				var intVal = Convert.ToInt32(value);
				crmRecord[mapping.LogicalName] =
					(intVal == -1 && clearMode == ClearMode.Convention)
						? null
						: new OptionSetValue(intVal);
			}
			else if (value is decimal && crmProperty.PropertyType == typeof(Money))
			{
				crmRecord[mapping.LogicalName] =
					(value.Equals(decimal.MinValue) && clearMode == ClearMode.Convention)
						? null
						: new Money(((decimal?)value).Value);
			}
			else if (value is Guid && fieldName != "Id" && crmProperty.PropertyType == typeof(Guid?))
			{
				crmProperty.SetValue(crmRecord,
					(value.Equals(Guid.Empty) && clearMode == ClearMode.Convention)
						? null
						: (Guid?)value);
			}
			else if (value is LookupValue)
			{
				var lookupValue = (LookupValue)value;

				if (crmProperty.PropertyType == typeof(LookupValue))
				{
					crmProperty.SetValue(crmRecord,
						(lookupValue.Id.Equals(Guid.Empty) && clearMode == ClearMode.Convention)
							? null
							: value);
				}
				else if (crmProperty.PropertyType == typeof(EntityReference))
				{
					crmProperty.SetValue(crmRecord,
						(lookupValue.Id.Equals(Guid.Empty) && clearMode == ClearMode.Convention)
							? null
							: new EntityReference(lookupValue.EntityName, lookupValue.Id));
				}
			}
			else
			{
				// if clear mode is convention, and value fits convention, then clear
				if (clearMode == ClearMode.Convention
					&& ((value is DateTime && value.Equals(new DateTime(1970, 1, 1)))
						|| (value is int && value.Equals(int.MinValue))
						|| (value is long && value.Equals(long.MinValue))
						|| (value is decimal && value.Equals(decimal.MinValue))
						|| (value is double && value.Equals(double.MinValue))
						|| (value is Array && (value as Array).Length <= 0)
						|| (value is string && value.Equals(""))))
				{
					crmProperty.SetValue(crmRecord, null);
				}
				else
				{
					crmProperty.SetValue(crmRecord, value);
				}
			}
		}

		/// <summary>
		///     Gets the property from the CRM entity that corresponds to this field -- same mapping
		/// </summary>
		/// <typeparam name="TCrmEntity">The type of the entity.</typeparam>
		/// <param name="mapping">The mapping.</param>
		/// <returns></returns>
		private static PropertyInfo GetCrmProperty<TCrmEntity>(CrmFieldMappingAttribute mapping)
			where TCrmEntity : Entity
		{
			var crmProperty = typeof(TCrmEntity).GetProperties()
				.FirstOrDefault(propertyQ =>
				{
					var fieldAttribute = propertyQ
						.GetCustomAttributes<AttributeLogicalNameAttribute>(true)
						.FirstOrDefault();

					return fieldAttribute != null
						   && fieldAttribute.LogicalName == mapping.LogicalName;
				});

			return crmProperty;
		}

		private static void SetRelationInCrmRecord<TCrmEntity>(object value, TCrmEntity crmRecord,
			CrmRelationMappingAttribute mapping, ClearMode? clearMode = null)
			where TCrmEntity : Entity
		{
			var crmRelation = GetCrmRelation<TCrmEntity>(mapping);

			// if relation not found, then the entities don't map correctly
			if (crmRelation == null)
			{
				throw new Exception($"Entity doesn't map to CRM entity '{typeof(TCrmEntity).Name}'.");
			}

			object convertedValue = null;

			var type = TypeHelpers.GetType(mapping.RelatedEntityName);

			if (type == null)
			{
				throw new TypeLoadException($"Could not find type {mapping.RelatedEntityName} to convert contract record.");
			}

			// x-N relation
			if (value is Array)
			{
				var relatedRecords = (Array)value;
				// create an array for the related entities of the appropriate type
				var crmRelatedRecords = Array.CreateInstance(type, relatedRecords.Length);

				// create the method reference that will be used to convert the related entities to the CRM entities
				var method = GetConversionMethod(relatedRecords.GetValue(0), type);

				if (method == null)
				{
					return;
				}

				// convert all entities to CRM entities
				var objectCrmRelatedRecords =
					(from object relatedRecord in relatedRecords
					 select method.Invoke(relatedRecord, new object[] { clearMode })).ToArray();

				// copy the object entities to the array of the specific type
				Array.Copy(objectCrmRelatedRecords, crmRelatedRecords, crmRelatedRecords.Length);
				convertedValue = crmRelatedRecords;
			}
			else if (value is EntityContract)
			{
				// N-1 relation
				var method = GetConversionMethod(value, type);

				if (method == null)
				{
					return;
				}

				convertedValue = method.Invoke(value, new object[] { clearMode });
			}

			// set the related entities value in the CRM entity
			crmRelation.SetValue(crmRecord, convertedValue);
		}

		private static MethodInfo GetConversionMethod(object entity, Type relatedType)
		{
			return entity.GetType().GetMethod("ConvertTo")?.MakeGenericMethod(relatedType);
		}

		/// <summary>
		///     Gets the relation from the CRM entity that corresponds to this relation -- same mapping
		/// </summary>
		/// <typeparam name="TCrmEntity">The type of the entity.</typeparam>
		/// <param name="mapping">The mapping.</param>
		/// <returns></returns>
		private static PropertyInfo GetCrmRelation<TCrmEntity>(CrmRelationMappingAttribute mapping)
			where TCrmEntity : Entity
		{
			var crmRelation = typeof(TCrmEntity).GetProperties()
				.FirstOrDefault(propertyQ =>
				{
					var relationAttribute = propertyQ
						.GetCustomAttributes<RelationshipSchemaNameAttribute>(true)
						.FirstOrDefault();

					return relationAttribute != null
						   && relationAttribute.SchemaName == mapping.SchemaName
						   && relationAttribute.PrimaryEntityRole == (EntityRole?)mapping.Role;
				});

			return crmRelation;
		}
	}

	#endregion


	#region Actions

	#endregion

	#region Known Types

	#endregion


	#region Escuela

	/// <summary>
	/// 'Account'.<br />
	/// Empresa que representa a un cliente o cliente potencial. La empresa a la que se factura en transacciones comerciales.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract, EntityLogicalName("account")]
	public partial class Escuela : GeneratedEntity<Escuela.RelationName>
	{
		public Escuela() : base(EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Escuela(string[] keys, object[] values) : base(keys, values, EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Escuela(object obj, Type limitingType) : base(obj, limitingType, EntityLogicalName)
		{ }

		public const string DisplayName = "Escuela";
		public const string SchemaName = "Account";
		public const string EntityLogicalName = "account";
		public const int EntityTypeCode = 1;

		public class RelationName : RelationNameBase
		{
			public RelationName(string name) : base(name)
			{ }
		}

		#region Alternate Keys

		public void AddNombredelaescuelaKey(string value) { KeyAttributes.Add("name", value); }

		#endregion

		#region Attributes

		[AttributeLogicalName("accountid")]
		public override System.Guid Id
		{
			get => (CuentaId == null || CuentaId == Guid.Empty) ? base.Id : CuentaId.GetValueOrDefault();
			set
			{
				if (value == Guid.Empty)
				{
					Attributes.Remove("accountid");
					base.Id = value;
				}
				else
				{
					CuentaId = value;
				}
			}
		}

		/// <summary>
		///  
		/// 'AccountCategoryCode'.<br />
		/// Seleccione una categoría para indicar si la cuenta del cliente es estándar o preferida.
		/// </summary>
		[AttributeLogicalName("accountcategorycode")]
		public CategoriaEnum? Categoria
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountcategorycode");
				return (CategoriaEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("accountcategorycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("accountcategorycode", value);
			}
		}

		public IDictionary<int, string> CategoriaLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountcategorycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("accountcategorycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'AccountClassificationCode'.<br />
		/// Seleccione un código de clasificación para indicar el valor potencial de la cuenta del cliente basado en las proyecciones de rentabilidad de la inversión, nivel de cooperación, duración de ciclo de ventas u otros criterios.
		/// </summary>
		[AttributeLogicalName("accountclassificationcode")]
		public ClasificacionEnum? Clasificacion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountclassificationcode");
				return (ClasificacionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("accountclassificationcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("accountclassificationcode", value);
			}
		}

		public IDictionary<int, string> ClasificacionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountclassificationcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("accountclassificationcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'AccountId'.<br />
		/// Identificador único de la cuenta.
		/// </summary>
		[AttributeLogicalName("accountid")]
		public Guid? CuentaId
		{
			get
			{
				var value = GetAttributeValue<Guid?>("accountid");
				return value;
			}
			set
			{
				if (value != null)
					SetAttributeValue("accountid", value);
				if (value != null) base.Id = value.Value;
				else Id = System.Guid.Empty;
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'AccountNumber'.<br />
		/// Escriba un número o un código de identificación de la cuenta para buscar e identificar rápidamente la cuenta en las vistas del sistema.
		/// </summary>
		[AttributeLogicalName("accountnumber"), MaxLength(20), StringLength(20)]
		public string Numerodecuenta
		{
			get
			{
				var value = GetAttributeValue<string>("accountnumber");
				return value;
			}
			set
			{
				SetAttributeValue("accountnumber", value);
			}
		}

		/// <summary>
		///  
		/// 'AccountRatingCode'.<br />
		/// Seleccione una clasificación para indicar el valor de la cuenta del cliente.
		/// </summary>
		[AttributeLogicalName("accountratingcode")]
		public NiveldecuentaEnum? Niveldecuenta
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountratingcode");
				return (NiveldecuentaEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("accountratingcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("accountratingcode", value);
			}
		}

		public IDictionary<int, string> NiveldecuentaLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountratingcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("accountratingcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'Address1_AddressTypeCode'.<br />
		/// Seleccione el tipo de dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_addresstypecode")]
		public Direccion1tipodedireccionEnum? Direccion1tipodedireccion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_addresstypecode");
				return (Direccion1tipodedireccionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_addresstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_addresstypecode", value);
			}
		}

		public IDictionary<int, string> Direccion1tipodedireccionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_addresstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_addresstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address1_City'.<br />
		/// Escriba la ciudad de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_city"), MaxLength(80), StringLength(80)]
		public string Direccion1ciudad
		{
			get
			{
				var value = GetAttributeValue<string>("address1_city");
				return value;
			}
			set
			{
				SetAttributeValue("address1_city", value);
			}
		}

		/// <summary>
		/// [MaxLength=1000] 
		/// 'Address1_Composite'.<br />
		/// Muestra la dirección principal completa.
		/// </summary>
		[AttributeLogicalName("address1_composite"), MaxLength(1000), StringLength(1000)]
		public string Direccion1
		{
			get
			{
				var value = GetAttributeValue<string>("address1_composite");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address1_Country'.<br />
		/// Escriba el país o la región de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_country"), MaxLength(80), StringLength(80)]
		public string Direccion1paisoregion
		{
			get
			{
				var value = GetAttributeValue<string>("address1_country");
				return value;
			}
			set
			{
				SetAttributeValue("address1_country", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_County'.<br />
		/// Escriba el condado de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_county"), MaxLength(50), StringLength(50)]
		public string Direccion1condado
		{
			get
			{
				var value = GetAttributeValue<string>("address1_county");
				return value;
			}
			set
			{
				SetAttributeValue("address1_county", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Fax'.<br />
		/// Escriba el número de fax asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_fax"), MaxLength(50), StringLength(50)]
		public string Direccion1fax
		{
			get
			{
				var value = GetAttributeValue<string>("address1_fax");
				return value;
			}
			set
			{
				SetAttributeValue("address1_fax", value);
			}
		}

		/// <summary>
		///  
		/// 'Address1_FreightTermsCode'.<br />
		/// Seleccione las condiciones de flete de la dirección principal para asegurarse de que los pedidos de envío se procesan correctamente.
		/// </summary>
		[AttributeLogicalName("address1_freighttermscode")]
		public Direccion1condicionesdefleteEnum? Direccion1condicionesdeflete
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_freighttermscode");
				return (Direccion1condicionesdefleteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_freighttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_freighttermscode", value);
			}
		}

		public IDictionary<int, string> Direccion1condicionesdefleteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_freighttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_freighttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-90, 90)] 
		/// 'Address1_Latitude'.<br />
		/// Escriba el valor de latitud de la dirección principal para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address1_latitude"), Range(-90, 90)]
		public double? Direccion1latitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address1_latitude");
				return value;
			}
			set
			{
				SetAttributeValue("address1_latitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line1'.<br />
		/// Escriba la primera línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line1"), MaxLength(250), StringLength(250)]
		public string Direccion1calle1
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line1");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line1", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line2'.<br />
		/// Escriba la segunda línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line2"), MaxLength(250), StringLength(250)]
		public string Direccion1calle2
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line2");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line2", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line3'.<br />
		/// Escriba la tercera línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line3"), MaxLength(250), StringLength(250)]
		public string Direccion1calle3
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line3");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line3", value);
			}
		}

		/// <summary>
		/// [Range(-180, 180)] 
		/// 'Address1_Longitude'.<br />
		/// Escriba el valor de longitud de la dirección principal para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address1_longitude"), Range(-180, 180)]
		public double? Direccion1longitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address1_longitude");
				return value;
			}
			set
			{
				SetAttributeValue("address1_longitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'Address1_Name'.<br />
		/// Escriba un nombre descriptivo para la dirección principal, como Oficinas centrales.
		/// </summary>
		[AttributeLogicalName("address1_name"), MaxLength(200), StringLength(200)]
		public string Direccion1nombre
		{
			get
			{
				var value = GetAttributeValue<string>("address1_name");
				return value;
			}
			set
			{
				SetAttributeValue("address1_name", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address1_PostalCode'.<br />
		/// Escriba el código postal de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_postalcode"), MaxLength(20), StringLength(20)]
		public string Direccion1codigopostal
		{
			get
			{
				var value = GetAttributeValue<string>("address1_postalcode");
				return value;
			}
			set
			{
				SetAttributeValue("address1_postalcode", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address1_PostOfficeBox'.<br />
		/// Escriba el número de apartado de correos de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_postofficebox"), MaxLength(20), StringLength(20)]
		public string Direccion1apartadodecorreos
		{
			get
			{
				var value = GetAttributeValue<string>("address1_postofficebox");
				return value;
			}
			set
			{
				SetAttributeValue("address1_postofficebox", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address1_PrimaryContactName'.<br />
		/// Escriba el nombre del contacto principal en la dirección principal de la cuenta.
		/// </summary>
		[AttributeLogicalName("address1_primarycontactname"), MaxLength(100), StringLength(100)]
		public string Direccion1nomcontactoppal
		{
			get
			{
				var value = GetAttributeValue<string>("address1_primarycontactname");
				return value;
			}
			set
			{
				SetAttributeValue("address1_primarycontactname", value);
			}
		}

		/// <summary>
		///  
		/// 'Address1_ShippingMethodCode'.<br />
		/// Seleccione un método de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("address1_shippingmethodcode")]
		public Direccion1mododeenvioEnum? Direccion1mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_shippingmethodcode");
				return (Direccion1mododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> Direccion1mododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_StateOrProvince'.<br />
		/// Escriba el estado o la provincia de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_stateorprovince"), MaxLength(50), StringLength(50)]
		public string Direccion1estadooprovincia
		{
			get
			{
				var value = GetAttributeValue<string>("address1_stateorprovince");
				return value;
			}
			set
			{
				SetAttributeValue("address1_stateorprovince", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone1'.<br />
		/// Escriba el número de teléfono principal asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone1"), MaxLength(50), StringLength(50)]
		public string DireccionTelefono
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone2'.<br />
		/// Escriba un segundo número de teléfono asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone2"), MaxLength(50), StringLength(50)]
		public string Direccion1telefono2
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone3'.<br />
		/// Escriba un tercer número de teléfono asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone3"), MaxLength(50), StringLength(50)]
		public string Direccion1telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone3", value);
			}
		}

		/// <summary>
		/// [MaxLength=4] 
		/// 'Address1_UPSZone'.<br />
		/// Escriba la zona de UPS de la dirección principal para asegurarse de que los costes de envío se calculan correctamente y las entregas se realizan puntualmente si se expiden a través de UPS.
		/// </summary>
		[AttributeLogicalName("address1_upszone"), MaxLength(4), StringLength(4)]
		public string Direccion1zonadeUPS
		{
			get
			{
				var value = GetAttributeValue<string>("address1_upszone");
				return value;
			}
			set
			{
				SetAttributeValue("address1_upszone", value);
			}
		}

		/// <summary>
		/// [Range(-1500, 1500)] 
		/// 'Address1_UTCOffset'.<br />
		/// Seleccione la zona horaria, o desplazamiento de UTC, para esta dirección de modo que otras personas puedan consultarla cuando se pongan en contacto con alguien en esta dirección.
		/// </summary>
		[AttributeLogicalName("address1_utcoffset"), Range(-1500, 1500)]
		public int? Direccion1desplazdeUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("address1_utcoffset");
				return value;
			}
			set
			{
				SetAttributeValue("address1_utcoffset", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_AddressTypeCode'.<br />
		/// Seleccione el tipo de dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_addresstypecode")]
		public Direccion2tipodedireccionEnum? Direccion2tipodedireccion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_addresstypecode");
				return (Direccion2tipodedireccionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_addresstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_addresstypecode", value);
			}
		}

		public IDictionary<int, string> Direccion2tipodedireccionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_addresstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_addresstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address2_City'.<br />
		/// Escriba la ciudad de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_city"), MaxLength(80), StringLength(80)]
		public string Direccion2ciudad
		{
			get
			{
				var value = GetAttributeValue<string>("address2_city");
				return value;
			}
			set
			{
				SetAttributeValue("address2_city", value);
			}
		}

		/// <summary>
		/// [MaxLength=1000] 
		/// 'Address2_Composite'.<br />
		/// Muestra la dirección secundaria completa.
		/// </summary>
		[AttributeLogicalName("address2_composite"), MaxLength(1000), StringLength(1000)]
		public string Direccion2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_composite");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address2_Country'.<br />
		/// Escriba el país o la región de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_country"), MaxLength(80), StringLength(80)]
		public string Direccion2paisoregion
		{
			get
			{
				var value = GetAttributeValue<string>("address2_country");
				return value;
			}
			set
			{
				SetAttributeValue("address2_country", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_County'.<br />
		/// Escriba el condado de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_county"), MaxLength(50), StringLength(50)]
		public string Direccion2condado
		{
			get
			{
				var value = GetAttributeValue<string>("address2_county");
				return value;
			}
			set
			{
				SetAttributeValue("address2_county", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Fax'.<br />
		/// Escriba el número de fax asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_fax"), MaxLength(50), StringLength(50)]
		public string Direccion2fax
		{
			get
			{
				var value = GetAttributeValue<string>("address2_fax");
				return value;
			}
			set
			{
				SetAttributeValue("address2_fax", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_FreightTermsCode'.<br />
		/// Seleccione las condiciones de flete de la dirección secundaria para asegurarse de que los pedidos de envío se procesan correctamente.
		/// </summary>
		[AttributeLogicalName("address2_freighttermscode")]
		public Direccion2condicionesdefleteEnum? Direccion2condicionesdeflete
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_freighttermscode");
				return (Direccion2condicionesdefleteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_freighttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_freighttermscode", value);
			}
		}

		public IDictionary<int, string> Direccion2condicionesdefleteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_freighttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_freighttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-90, 90)] 
		/// 'Address2_Latitude'.<br />
		/// Escriba el valor de latitud de la dirección secundaria para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address2_latitude"), Range(-90, 90)]
		public double? Direccion2latitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address2_latitude");
				return value;
			}
			set
			{
				SetAttributeValue("address2_latitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line1'.<br />
		/// Escriba la primera línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line1"), MaxLength(250), StringLength(250)]
		public string Direccion2calle1
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line1");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line1", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line2'.<br />
		/// Escriba la segunda línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line2"), MaxLength(250), StringLength(250)]
		public string Direccion2calle2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line2");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line2", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line3'.<br />
		/// Escriba la tercera línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line3"), MaxLength(250), StringLength(250)]
		public string Direccion2calle3
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line3");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line3", value);
			}
		}

		/// <summary>
		/// [Range(-180, 180)] 
		/// 'Address2_Longitude'.<br />
		/// Escriba el valor de longitud de la dirección secundaria para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address2_longitude"), Range(-180, 180)]
		public double? Direccion2longitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address2_longitude");
				return value;
			}
			set
			{
				SetAttributeValue("address2_longitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'Address2_Name'.<br />
		/// Escriba un nombre descriptivo para la dirección secundaria, como Oficinas centrales.
		/// </summary>
		[AttributeLogicalName("address2_name"), MaxLength(200), StringLength(200)]
		public string Direccion2nombre
		{
			get
			{
				var value = GetAttributeValue<string>("address2_name");
				return value;
			}
			set
			{
				SetAttributeValue("address2_name", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address2_PostalCode'.<br />
		/// Escriba el código postal de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_postalcode"), MaxLength(20), StringLength(20)]
		public string Direccion2codigopostal
		{
			get
			{
				var value = GetAttributeValue<string>("address2_postalcode");
				return value;
			}
			set
			{
				SetAttributeValue("address2_postalcode", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address2_PostOfficeBox'.<br />
		/// Escriba el número de apartado de correos de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_postofficebox"), MaxLength(20), StringLength(20)]
		public string Direccion2apartadodecorreos
		{
			get
			{
				var value = GetAttributeValue<string>("address2_postofficebox");
				return value;
			}
			set
			{
				SetAttributeValue("address2_postofficebox", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address2_PrimaryContactName'.<br />
		/// Escriba el nombre del contacto principal en la dirección secundaria de la cuenta.
		/// </summary>
		[AttributeLogicalName("address2_primarycontactname"), MaxLength(100), StringLength(100)]
		public string Direccion2nomcontactoppal
		{
			get
			{
				var value = GetAttributeValue<string>("address2_primarycontactname");
				return value;
			}
			set
			{
				SetAttributeValue("address2_primarycontactname", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_ShippingMethodCode'.<br />
		/// Seleccione un método de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("address2_shippingmethodcode")]
		public Direccion2mododeenvioEnum? Direccion2mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_shippingmethodcode");
				return (Direccion2mododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> Direccion2mododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_StateOrProvince'.<br />
		/// Escriba el estado o la provincia de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_stateorprovince"), MaxLength(50), StringLength(50)]
		public string Direccion2estadooprovincia
		{
			get
			{
				var value = GetAttributeValue<string>("address2_stateorprovince");
				return value;
			}
			set
			{
				SetAttributeValue("address2_stateorprovince", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone1'.<br />
		/// Escriba el número de teléfono principal asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone1"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono1
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone2'.<br />
		/// Escriba un segundo número de teléfono asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone2"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone3'.<br />
		/// Escriba un tercer número de teléfono asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone3"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone3", value);
			}
		}

		/// <summary>
		/// [MaxLength=4] 
		/// 'Address2_UPSZone'.<br />
		/// Escriba la zona de UPS de la dirección secundaria para asegurarse de que los costes de envío se calculan correctamente y las entregas se realizan puntualmente si se expiden a través de UPS.
		/// </summary>
		[AttributeLogicalName("address2_upszone"), MaxLength(4), StringLength(4)]
		public string Direccion2zonadeUPS
		{
			get
			{
				var value = GetAttributeValue<string>("address2_upszone");
				return value;
			}
			set
			{
				SetAttributeValue("address2_upszone", value);
			}
		}

		/// <summary>
		/// [Range(-1500, 1500)] 
		/// 'Address2_UTCOffset'.<br />
		/// Seleccione la zona horaria, o desplazamiento de UTC, para esta dirección de modo que otras personas puedan consultarla cuando se pongan en contacto con alguien en esta dirección.
		/// </summary>
		[AttributeLogicalName("address2_utcoffset"), Range(-1500, 1500)]
		public int? Direccion2desplazdeUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("address2_utcoffset");
				return value;
			}
			set
			{
				SetAttributeValue("address2_utcoffset", value);
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging30'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging30"), Range(0, 100000000000000)]
		public decimal? Vence30
		{
			get
			{
				var value = GetAttributeValue<Money>("aging30");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging30_Base'.<br />
		/// La divisa base equivalente al campo de vencimiento 30.
		/// </summary>
		[AttributeLogicalName("aging30_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence30base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging30_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging60'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging60"), Range(0, 100000000000000)]
		public decimal? Vence60
		{
			get
			{
				var value = GetAttributeValue<Money>("aging60");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging60_Base'.<br />
		/// La divisa base equivalente al campo de vencimiento 60.
		/// </summary>
		[AttributeLogicalName("aging60_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence60base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging60_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging90'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging90"), Range(0, 100000000000000)]
		public decimal? Vence90
		{
			get
			{
				var value = GetAttributeValue<Money>("aging90");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging90_Base'.<br />
		/// La divisa base equivalente al campo de vencimiento 90.
		/// </summary>
		[AttributeLogicalName("aging90_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence90base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging90_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'BusinessTypeCode'.<br />
		/// Seleccione la designación legal u otro tipo de negocio de la cuenta para contratos o generación de informes.
		/// </summary>
		[AttributeLogicalName("businesstypecode")]
		public TipodenegocioEnum? Tipodenegocio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("businesstypecode");
				return (TipodenegocioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("businesstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("businesstypecode", value);
			}
		}

		public IDictionary<int, string> TipodenegocioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("businesstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("businesstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'CreatedBy'.<br />
		/// Muestra quién creó el registro.
		/// </summary>
		[AttributeLogicalName("createdby")]
		public Guid? Autor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Id;
			}
		}

		public EntityReference AutorReference => Autor == null ? null : GetAttributeValue<EntityReference>("createdby");

		public string AutorName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutorLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedByExternalParty'.<br />
		/// Muestra la parte externa que creó el registro.
		/// </summary>
		[AttributeLogicalName("createdbyexternalparty")]
		public Guid? Creadoporparteexterna
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdbyexternalparty");
				return value?.Id;
			}
		}

		public string CreadoporparteexternaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdbyexternalparty");
				return value?.Name;
			}
		}

		public IDictionary<int, string> CreadoporparteexternaLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedOn'.<br />
		/// Muestra la fecha y la hora en que se creó el registro. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("createdon")]
		public DateTime? Fechadecreacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("createdon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'CreatedOnBehalfBy'.<br />
		/// Muestra quién creó el registro en nombre de otro usuario.
		/// </summary>
		[AttributeLogicalName("createdonbehalfby")]
		public Guid? Autordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference AutordelegadoReference => Autordelegado == null ? null : GetAttributeValue<EntityReference>("createdonbehalfby");

		public string AutordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutordelegadoLabels { get; set; }

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'CreditLimit'.<br />
		/// Escriba el límite de crédito de la cuenta. Es una referencia útil cuando se abordan problemas de facturación y contabilidad con el cliente.
		/// </summary>
		[AttributeLogicalName("creditlimit"), Range(0, 100000000000000)]
		public decimal? Limitedelcredito
		{
			get
			{
				var value = GetAttributeValue<Money>("creditlimit");
				return value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("creditlimit", new Money(value.Value));
				else
					SetAttributeValue("creditlimit", value);
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'CreditLimit_Base'.<br />
		/// Muestra el límite de crédito convertido a la divisa base predeterminada del sistema para la generación de informes.
		/// </summary>
		[AttributeLogicalName("creditlimit_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Limitedecreditobase
		{
			get
			{
				var value = GetAttributeValue<Money>("creditlimit_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'CreditOnHold'.<br />
		/// Seleccione si el crédito de la cuenta está suspendido. Es una referencia útil al abordar problemas de facturación y contabilidad con el cliente.
		/// </summary>
		[AttributeLogicalName("creditonhold")]
		public bool? Suspensiondecredito
		{
			get
			{
				var value = GetAttributeValue<bool?>("creditonhold");
				return value;
			}
			set
			{
				SetAttributeValue("creditonhold", value);
			}
		}

		public IDictionary<int, string> SuspensiondecreditoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("creditonhold");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("creditonhold", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'CustomerSizeCode'.<br />
		/// Seleccione la categoría de tamaño o el rango de la cuenta para segmentación y generación de informes.
		/// </summary>
		[AttributeLogicalName("customersizecode")]
		public TamanodelclienteEnum? Tamanodelcliente
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customersizecode");
				return (TamanodelclienteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("customersizecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("customersizecode", value);
			}
		}

		public IDictionary<int, string> TamanodelclienteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customersizecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("customersizecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'CustomerTypeCode'.<br />
		/// Seleccione la categoría que mejor describe la relación entre la cuenta y la organización.
		/// </summary>
		[AttributeLogicalName("customertypecode")]
		public TipoderelacionEnum? Tipoderelacion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customertypecode");
				return (TipoderelacionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("customertypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("customertypecode", value);
			}
		}

		public IDictionary<int, string> TipoderelacionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customertypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("customertypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DefaultPriceLevelId'.<br />
		/// Elija la lista de precios predeterminada asociada a la cuenta para asegurarse de que se aplican los precios correctos de los productos para este cliente en oportunidades de ventas, ofertas y pedidos.
		/// </summary>
		[AttributeLogicalName("defaultpricelevelid")]
		public Guid? Listadeprecios
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("defaultpricelevelid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("defaultpricelevelid", new EntityReference("pricelevel", value.Value));
				else
					SetAttributeValue("defaultpricelevelid", value);
			}
		}

		public string ListadepreciosName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("defaultpricelevelid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ListadepreciosLabels { get; set; }

		/// <summary>
		/// [MaxLength=2000] 
		/// 'Description'.<br />
		/// Escriba información adicional para describir la cuenta, por ejemplo, un extracto del sitio web de la compañía.
		/// </summary>
		[AttributeLogicalName("description"), MaxLength(2000), StringLength(2000)]
		public string Descripcion
		{
			get
			{
				var value = GetAttributeValue<string>("description");
				return value;
			}
			set
			{
				SetAttributeValue("description", value);
			}
		}

		/// <summary>
		///  
		/// 'DoNotBulkEMail'.<br />
		/// Seleccione si la cuenta permite enviar correo electrónico en masa a través de campañas. Si selecciona No permitir, la cuenta podrá agregarse a listas de marketing, pero quedará excluida del correo electrónico.
		/// </summary>
		[AttributeLogicalName("donotbulkemail")]
		public bool? Nopermitircorreoelecenmasa
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkemail");
				return value;
			}
			set
			{
				SetAttributeValue("donotbulkemail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoelecenmasaLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotbulkemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotBulkPostalMail'.<br />
		/// Seleccione si la cuenta permite enviar correo postal masivo a través de campañas de marketing o campañas exprés. Si selecciona No permitir, la cuenta podrá agregarse a listas de marketing, pero quedará excluida del correo postal.
		/// </summary>
		[AttributeLogicalName("donotbulkpostalmail")]
		public bool? Nopermitircorreoenmasa
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkpostalmail");
				return value;
			}
			set
			{
				SetAttributeValue("donotbulkpostalmail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoenmasaLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkpostalmail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotbulkpostalmail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotEMail'.<br />
		/// Seleccione si la cuenta permite enviar correo electrónico directo desde Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("donotemail")]
		public bool? Nopermitircorreoelectronico
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotemail");
				return value;
			}
			set
			{
				SetAttributeValue("donotemail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoelectronicoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotFax'.<br />
		/// Seleccione si la cuenta permite faxes. Si selecciona No permitir, la cuenta podrá quedar excluida de las actividades de fax distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotfax")]
		public bool? Nopermitirfaxes
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotfax");
				return value;
			}
			set
			{
				SetAttributeValue("donotfax", value);
			}
		}

		public IDictionary<int, string> NopermitirfaxesLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotfax");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotfax", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotPhone'.<br />
		/// Seleccione si la cuenta permite llamadas de teléfono. Si selecciona No permitir, la cuenta quedará excluida de las actividades de llamada de teléfono distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotphone")]
		public bool? Nopermitirllamadasdetelefono
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotphone");
				return value;
			}
			set
			{
				SetAttributeValue("donotphone", value);
			}
		}

		public IDictionary<int, string> NopermitirllamadasdetelefonoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotphone");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotphone", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotPostalMail'.<br />
		/// Seleccione si la cuenta permite correo directo. Si selecciona No permitir, la cuenta quedará excluida de las actividades de cartas distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotpostalmail")]
		public bool? Nopermitircorreo
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotpostalmail");
				return value;
			}
			set
			{
				SetAttributeValue("donotpostalmail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotpostalmail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotpostalmail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotSendMM'.<br />
		/// Seleccione si la cuenta acepta materiales de marketing, como folletos o catálogos.
		/// </summary>
		[AttributeLogicalName("donotsendmm")]
		public bool? Enviarmaterialesdemarketing
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotsendmm");
				return value;
			}
			set
			{
				SetAttributeValue("donotsendmm", value);
			}
		}

		public IDictionary<int, string> EnviarmaterialesdemarketingLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotsendmm");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotsendmm", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress1'.<br />
		/// Escriba la dirección de correo electrónico principal para la cuenta.
		/// </summary>
		[AttributeLogicalName("emailaddress1"), MaxLength(100), StringLength(100)]
		public string Correoelectronico
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress1");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress1", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress2'.<br />
		/// Escriba la dirección de correo electrónico secundaria para la cuenta.
		/// </summary>
		[AttributeLogicalName("emailaddress2"), MaxLength(100), StringLength(100)]
		public string Direcciondecorreoelectronico2
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress2");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress2", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress3'.<br />
		/// Escriba una dirección de correo electrónico alternativa para la cuenta.
		/// </summary>
		[AttributeLogicalName("emailaddress3"), MaxLength(100), StringLength(100)]
		public string Direcciondecorreoelectronico3
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress3");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress3", value);
			}
		}

		/// <summary>
		///  
		/// 'EntityImage'.<br />
		/// Muestra la imagen predeterminada del registro.
		/// </summary>
		[AttributeLogicalName("entityimage"), MaxLength(10240000), MaxWidth(144), MaxHeight(144)]
		public byte[] Imagenpredeterminada
		{
			get
			{
				var value = GetAttributeValue<byte[]>("entityimage");
				return value;
			}
			set
			{
				SetAttributeValue("entityimage", value);
			}
		}

		/// <summary>
		///  
		/// 'EntityImageId'.<br />
		/// Para uso interno.
		/// </summary>
		[AttributeLogicalName("entityimageid")]
		public Guid? Identificadordeimagendelaentidad
		{
			get
			{
				var value = GetAttributeValue<Guid?>("entityimageid");
				return value;
			}
		}

		/// <summary>
		/// [Range(0.0000000001, 100000000000)] 
		/// 'ExchangeRate'.<br />
		/// Muestra la tasa de conversión de la divisa del registro. El tipo de cambio se utiliza para convertir todos los campos de dinero del registro desde la divisa local hasta la divisa predeterminada del sistema.
		/// </summary>
		[AttributeLogicalName("exchangerate"), Range(0.0000000001, 100000000000)]
		public decimal? Tipodecambio
		{
			get
			{
				var value = GetAttributeValue<decimal?>("exchangerate");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Fax'.<br />
		/// Escriba el número de fax de la cuenta.
		/// </summary>
		[AttributeLogicalName("fax"), MaxLength(50), StringLength(50)]
		public string Fax
		{
			get
			{
				var value = GetAttributeValue<string>("fax");
				return value;
			}
			set
			{
				SetAttributeValue("fax", value);
			}
		}

		/// <summary>
		///  
		/// 'FollowEmail'.<br />
		/// Información sobre si se permite el seguimiento de la actividad de correo, por ejemplo, abrir, ver datos adjuntos y hacer clic en vínculos de correos enviados a la cuenta.
		/// </summary>
		[AttributeLogicalName("followemail")]
		public bool? Seguiractividaddecorreo
		{
			get
			{
				var value = GetAttributeValue<bool?>("followemail");
				return value;
			}
			set
			{
				SetAttributeValue("followemail", value);
			}
		}

		public IDictionary<int, string> SeguiractividaddecorreoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("followemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("followemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'FtpSiteURL'.<br />
		/// Escriba la dirección URL del sitio FTP de la cuenta para permitir a los usuarios acceder a datos y compartir documentos.
		/// </summary>
		[AttributeLogicalName("ftpsiteurl"), MaxLength(200), StringLength(200)]
		public string SitiodeFTP
		{
			get
			{
				var value = GetAttributeValue<string>("ftpsiteurl");
				return value;
			}
			set
			{
				SetAttributeValue("ftpsiteurl", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'ImportSequenceNumber'.<br />
		/// Identificador único de la importación o la migración de datos que creó este registro.
		/// </summary>
		[AttributeLogicalName("importsequencenumber"), Range(-2147483648, 2147483647)]
		public int? Numerodesecuenciadeimportacion
		{
			get
			{
				var value = GetAttributeValue<int?>("importsequencenumber");
				return value;
			}
			set
			{
				SetAttributeValue("importsequencenumber", value);
			}
		}

		/// <summary>
		///  
		/// 'IndustryCode'.<br />
		/// Seleccione el sector principal de la cuenta para su uso en segmentación de mercados y análisis demográficos.
		/// </summary>
		[AttributeLogicalName("industrycode")]
		public SectorEnum? Sector
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("industrycode");
				return (SectorEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("industrycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("industrycode", value);
			}
		}

		public IDictionary<int, string> SectorLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("industrycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("industrycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'LastOnHoldTime'.<br />
		/// Contiene la marca de fecha y hora del último período de retención.
		/// </summary>
		[AttributeLogicalName("lastonholdtime")]
		public DateTime? Ultimoperiododeretencion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("lastonholdtime");
				return value;
			}
			set
			{
				SetAttributeValue("lastonholdtime", value);
			}
		}

		/// <summary>
		///  
		/// 'LastUsedInCampaign'.<br />
		/// Muestra la fecha en la que la cuenta se incluyó por última vez en una campaña de marketing o una campaña exprés.
		/// </summary>
		[AttributeLogicalName("lastusedincampaign")]
		public DateTime? Ultimodiaincluidoenlacampana
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("lastusedincampaign");
				return value;
			}
			set
			{
				SetAttributeValue("lastusedincampaign", value);
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'MarketCap'.<br />
		/// Escriba la capitalización de mercado de la cuenta para identificar los recursos propios de la compañía, que servirán de indicadores en análisis de rendimiento financiero.
		/// </summary>
		[AttributeLogicalName("marketcap"), Range(0, 100000000000000)]
		public decimal? Capitalizaciondemercado
		{
			get
			{
				var value = GetAttributeValue<Money>("marketcap");
				return value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("marketcap", new Money(value.Value));
				else
					SetAttributeValue("marketcap", value);
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'MarketCap_Base'.<br />
		/// Muestra la capitalización de mercado convertida a la divisa base predeterminada del sistema.
		/// </summary>
		[AttributeLogicalName("marketcap_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Capitalizaciondemercadobase
		{
			get
			{
				var value = GetAttributeValue<Money>("marketcap_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'MarketingOnly'.<br />
		/// Indica si es solo para marketing
		/// </summary>
		[AttributeLogicalName("marketingonly")]
		public bool? Soloparamarketing
		{
			get
			{
				var value = GetAttributeValue<bool?>("marketingonly");
				return value;
			}
			set
			{
				SetAttributeValue("marketingonly", value);
			}
		}

		public IDictionary<int, string> SoloparamarketingLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("marketingonly");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("marketingonly", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'MasterId'.<br />
		/// Muestra la cuenta principal con la que se ha combinado la cuenta.
		/// </summary>
		[AttributeLogicalName("masterid")]
		public Guid? Idmaestro
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("masterid");
				return value?.Id;
			}
		}

		public EntityReference IdmaestroReference => Idmaestro == null ? null : GetAttributeValue<EntityReference>("masterid");

		public string IdmaestroName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("masterid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> IdmaestroLabels { get; set; }

		/// <summary>
		///  
		/// 'Merged'.<br />
		/// Muestra si la cuenta se ha combinado con otra cuenta.
		/// </summary>
		[AttributeLogicalName("merged")]
		public bool? Combinado
		{
			get
			{
				var value = GetAttributeValue<bool?>("merged");
				return value;
			}
		}

		public IDictionary<int, string> CombinadoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("merged");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("merged", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'ModifiedBy'.<br />
		/// Muestra quién actualizó el registro por última vez.
		/// </summary>
		[AttributeLogicalName("modifiedby")]
		public Guid? Modificadopor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Id;
			}
		}

		public EntityReference ModificadoporReference => Modificadopor == null ? null : GetAttributeValue<EntityReference>("modifiedby");

		public string ModificadoporName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedByExternalParty'.<br />
		/// Muestra la parte externa que modificó el registro.
		/// </summary>
		[AttributeLogicalName("modifiedbyexternalparty")]
		public Guid? Modificadoporparteexterna
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedbyexternalparty");
				return value?.Id;
			}
		}

		public string ModificadoporparteexternaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedbyexternalparty");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporparteexternaLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedOn'.<br />
		/// Muestra la fecha y la hora en que se actualizó el registro por última vez. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("modifiedon")]
		public DateTime? Fechademodificacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("modifiedon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'ModifiedOnBehalfBy'.<br />
		/// Muestra quién creó el registro en nombre de otro usuario.
		/// </summary>
		[AttributeLogicalName("modifiedonbehalfby")]
		public Guid? Modificadopordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference ModificadopordelegadoReference => Modificadopordelegado == null ? null : GetAttributeValue<EntityReference>("modifiedonbehalfby");

		public string ModificadopordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadopordelegadoLabels { get; set; }

		/// <summary>
		/// [Required][MaxLength=160] 
		/// 'Name'.<br />
		/// Escriba el nombre de la compañía o empresa.
		/// </summary>
		[AttributeLogicalName("name"), Required, MaxLength(160), StringLength(160)]
		public string Nombredelaescuela
		{
			get
			{
				var value = GetAttributeValue<string>("name");
				return value;
			}
			set
			{
				SetAttributeValue("name", value);
			}
		}

		[AttributeLogicalName("new_identifier"), MaxLength(100), StringLength(100)]
		public string Identificador
		{
			get
			{
				var value = GetAttributeValue<string>("new_identifier");
				return value;
			}
			set
			{
				SetAttributeValue("new_identifier", value);
			}
		}

		[AttributeLogicalName("new_numalumnos"), Range(-2147483648, 2147483647)]
		public int? NumAlumnos
		{
			get
			{
				var value = GetAttributeValue<int?>("new_numalumnos");
				return value;
			}
			set
			{
				SetAttributeValue("new_numalumnos", value);
			}
		}

		/// <summary>
		///  
		/// 'new_numalumnos_Date'.<br />
		/// Hora en que se actualizó el campo de informe Num Alumnos por última vez.
		/// </summary>
		[AttributeLogicalName("new_numalumnos_date")]
		public DateTime? NumAlumnosfechadeultimaactualizacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("new_numalumnos_date");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'new_numalumnos_State'.<br />
		/// Estado del campo de informe Num Alumnos.
		/// </summary>
		[AttributeLogicalName("new_numalumnos_state"), Range(-2147483648, 2147483647)]
		public int? NumAlumnosestado
		{
			get
			{
				var value = GetAttributeValue<int?>("new_numalumnos_state");
				return value;
			}
		}

		[AttributeLogicalName("new_numprofesores"), Range(-2147483648, 2147483647)]
		public int? NumProfesores
		{
			get
			{
				var value = GetAttributeValue<int?>("new_numprofesores");
				return value;
			}
			set
			{
				SetAttributeValue("new_numprofesores", value);
			}
		}

		/// <summary>
		///  
		/// 'new_numprofesores_Date'.<br />
		/// Hora en que se actualizó el campo de informe Num Profesores por última vez.
		/// </summary>
		[AttributeLogicalName("new_numprofesores_date")]
		public DateTime? NumProfesoresfechadeultimaactualizacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("new_numprofesores_date");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'new_numprofesores_State'.<br />
		/// Estado del campo de informe Num Profesores.
		/// </summary>
		[AttributeLogicalName("new_numprofesores_state"), Range(-2147483648, 2147483647)]
		public int? NumProfesoresestado
		{
			get
			{
				var value = GetAttributeValue<int?>("new_numprofesores_state");
				return value;
			}
		}

		/// <summary>
		/// [Range(0, 1000000000)] 
		/// 'NumberOfEmployees'.<br />
		/// Escriba el número de empleados que trabajan en la cuenta para su uso en segmentación de mercados y análisis demográficos.
		/// </summary>
		[AttributeLogicalName("numberofemployees"), Range(0, 1000000000)]
		public int? Numerodeempleados
		{
			get
			{
				var value = GetAttributeValue<int?>("numberofemployees");
				return value;
			}
			set
			{
				SetAttributeValue("numberofemployees", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OnHoldTime'.<br />
		/// Muestra durante cuánto tiempo, en minutos, se retuvo el registro.
		/// </summary>
		[AttributeLogicalName("onholdtime"), Range(-2147483648, 2147483647)]
		public int? Periododeretencionminutos
		{
			get
			{
				var value = GetAttributeValue<int?>("onholdtime");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OpenDeals'.<br />
		/// Número de oportunidades abiertas relacionadas con una cuenta y con sus cuentas secundarias.
		/// </summary>
		[AttributeLogicalName("opendeals"), Range(-2147483648, 2147483647)]
		public int? Operacionesabiertas
		{
			get
			{
				var value = GetAttributeValue<int?>("opendeals");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'OpenDeals_Date'.<br />
		/// Hora en que se actualizó el campo de informe Operaciones abiertas por última vez.
		/// </summary>
		[AttributeLogicalName("opendeals_date")]
		public DateTime? Operacionesabiertasfechadeultimaactualizacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("opendeals_date");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OpenDeals_State'.<br />
		/// Estado del campo de informe Operaciones abiertas.
		/// </summary>
		[AttributeLogicalName("opendeals_state"), Range(-2147483648, 2147483647)]
		public int? Operacionesabiertasestado
		{
			get
			{
				var value = GetAttributeValue<int?>("opendeals_state");
				return value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'OpenRevenue'.<br />
		/// Suma de los ingresos abiertos relacionados con una cuenta y con sus cuentas secundarias.
		/// </summary>
		[AttributeLogicalName("openrevenue"), Range(-922337203685477, 922337203685477)]
		public decimal? Ingresosabiertos
		{
			get
			{
				var value = GetAttributeValue<Money>("openrevenue");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'OpenRevenue_Base'.<br />
		/// Valor de Ingresos abiertos en divisa base.
		/// </summary>
		[AttributeLogicalName("openrevenue_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Ingresosabiertosbase
		{
			get
			{
				var value = GetAttributeValue<Money>("openrevenue_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'OpenRevenue_Date'.<br />
		/// Hora en que se actualizó el campo de informe Ingresos abiertos por última vez.
		/// </summary>
		[AttributeLogicalName("openrevenue_date")]
		public DateTime? Ingresosabiertosfechadeultimaactualizacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("openrevenue_date");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OpenRevenue_State'.<br />
		/// Estado del campo de informe Ingresos abiertos.
		/// </summary>
		[AttributeLogicalName("openrevenue_state"), Range(-2147483648, 2147483647)]
		public int? Ingresosabiertosestado
		{
			get
			{
				var value = GetAttributeValue<int?>("openrevenue_state");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'OriginatingLeadId'.<br />
		/// Muestra el cliente potencial a partir del cual se creó la cuenta si esta se creó convirtiendo un cliente potencial a Microsoft Dynamics 365. Se usa para relacionar la cuenta con datos del cliente potencial original para su uso en informes y análisis.
		/// </summary>
		[AttributeLogicalName("originatingleadid")]
		public Guid? Clientepotencialoriginal
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("originatingleadid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("originatingleadid", new EntityReference("lead", value.Value));
				else
					SetAttributeValue("originatingleadid", value);
			}
		}

		public EntityReference ClientepotencialoriginalReference => Clientepotencialoriginal == null ? null : GetAttributeValue<EntityReference>("originatingleadid");

		public string ClientepotencialoriginalName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("originatingleadid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ClientepotencialoriginalLabels { get; set; }

		/// <summary>
		///  
		/// 'OverriddenCreatedOn'.<br />
		/// Fecha y hora en que se migró el registro.
		/// </summary>
		[AttributeLogicalName("overriddencreatedon")]
		public DateTime? Fechadecreaciondelregistro
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("overriddencreatedon");
				return value;
			}
			set
			{
				SetAttributeValue("overriddencreatedon", value);
			}
		}

		/// <summary>
		///  
		/// 'OwnerId'.<br />
		/// Escriba el usuario o el equipo que está asignado para administrar el registro. Este campo se actualiza cada vez que se asigna el registro a otro usuario.
		/// </summary>
		[AttributeLogicalName("ownerid")]
		public EntityReference Propietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value;
			}
			set
			{
				SetAttributeValue("ownerid", value);
			}
		}

		public string PropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> PropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwnershipCode'.<br />
		/// Seleccione la estructura de propiedad de la cuenta, por ejemplo, pública o privada.
		/// </summary>
		[AttributeLogicalName("ownershipcode")]
		public PropiedadEnum? Propiedad
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("ownershipcode");
				return (PropiedadEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("ownershipcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("ownershipcode", value);
			}
		}

		public IDictionary<int, string> PropiedadLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("ownershipcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("ownershipcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'OwningBusinessUnit'.<br />
		/// Muestra la unidad de negocio a la que pertenece el propietario del registro.
		/// </summary>
		[AttributeLogicalName("owningbusinessunit")]
		public Guid? Unidaddenegociopropietaria
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Id;
			}
		}

		public string UnidaddenegociopropietariaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UnidaddenegociopropietariaLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningTeam'.<br />
		/// Identificador único del equipo propietario de la cuenta.
		/// </summary>
		[AttributeLogicalName("owningteam")]
		public Guid? Equipopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Id;
			}
		}

		public EntityReference EquipopropietarioReference => Equipopropietario == null ? null : GetAttributeValue<EntityReference>("owningteam");

		public string EquipopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Name;
			}
		}

		public IDictionary<int, string> EquipopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningUser'.<br />
		/// Identificador único del usuario propietario de la cuenta.
		/// </summary>
		[AttributeLogicalName("owninguser")]
		public Guid? Usuariopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Id;
			}
		}

		public EntityReference UsuariopropietarioReference => Usuariopropietario == null ? null : GetAttributeValue<EntityReference>("owninguser");

		public string UsuariopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'ParentAccountId'.<br />
		/// Elija la cuenta primaria asociada a esta cuenta para mostrar empresas primarias y secundarias en informes y análisis.
		/// </summary>
		[AttributeLogicalName("parentaccountid")]
		public Guid? Cuentaprimaria
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentaccountid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("parentaccountid", new EntityReference("account", value.Value));
				else
					SetAttributeValue("parentaccountid", value);
			}
		}

		public EntityReference CuentaprimariaReference => Cuentaprimaria == null ? null : GetAttributeValue<EntityReference>("parentaccountid");

		public string CuentaprimariaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentaccountid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> CuentaprimariaLabels { get; set; }

		/// <summary>
		///  
		/// 'ParticipatesInWorkflow'.<br />
		/// Solo para uso del sistema. Datos heredados del flujo de trabajo de Microsoft Dynamics CRM 3.0
		/// </summary>
		[AttributeLogicalName("participatesinworkflow")]
		public bool? Participaenflujodetrabajo
		{
			get
			{
				var value = GetAttributeValue<bool?>("participatesinworkflow");
				return value;
			}
			set
			{
				SetAttributeValue("participatesinworkflow", value);
			}
		}

		public IDictionary<int, string> ParticipaenflujodetrabajoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("participatesinworkflow");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("participatesinworkflow", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PaymentTermsCode'.<br />
		/// Seleccione las condiciones de pago para indicar cuándo debe pagar el cliente el importe total.
		/// </summary>
		[AttributeLogicalName("paymenttermscode")]
		public CondicionesdepagoEnum? Condicionesdepago
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("paymenttermscode");
				return (CondicionesdepagoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("paymenttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("paymenttermscode", value);
			}
		}

		public IDictionary<int, string> CondicionesdepagoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("paymenttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("paymenttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredAppointmentDayCode'.<br />
		/// Seleccione el día de la semana preferido para citas de servicio.
		/// </summary>
		[AttributeLogicalName("preferredappointmentdaycode")]
		public DiapreferidoEnum? Diapreferido
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmentdaycode");
				return (DiapreferidoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredappointmentdaycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredappointmentdaycode", value);
			}
		}

		public IDictionary<int, string> DiapreferidoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmentdaycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredappointmentdaycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredAppointmentTimeCode'.<br />
		/// Seleccione la hora preferida del día para citas de servicio.
		/// </summary>
		[AttributeLogicalName("preferredappointmenttimecode")]
		public HorapreferidaEnum? Horapreferida
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmenttimecode");
				return (HorapreferidaEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredappointmenttimecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredappointmenttimecode", value);
			}
		}

		public IDictionary<int, string> HorapreferidaLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmenttimecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredappointmenttimecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredContactMethodCode'.<br />
		/// Seleccione el método de contacto preferido.
		/// </summary>
		[AttributeLogicalName("preferredcontactmethodcode")]
		public MetododecontactopreferidoEnum? Metododecontactopreferido
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredcontactmethodcode");
				return (MetododecontactopreferidoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredcontactmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredcontactmethodcode", value);
			}
		}

		public IDictionary<int, string> MetododecontactopreferidoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredcontactmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredcontactmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredEquipmentId'.<br />
		/// Elija las instalaciones o el equipamiento de servicio preferido de la cuenta para asegurarse de que los servicios se programan correctamente para el cliente.
		/// </summary>
		[AttributeLogicalName("preferredequipmentid")]
		public Guid? Instalacequipampreferidos
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredequipmentid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredequipmentid", new EntityReference("equipment", value.Value));
				else
					SetAttributeValue("preferredequipmentid", value);
			}
		}

		public string InstalacequipampreferidosName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredequipmentid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> InstalacequipampreferidosLabels { get; set; }

		/// <summary>
		///  
		/// 'PreferredServiceId'.<br />
		/// Elija el servicio preferido de la cuenta para tenerlo como referencia cuando programe actividades de servicio.
		/// </summary>
		[AttributeLogicalName("preferredserviceid")]
		public Guid? Serviciopreferido
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredserviceid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredserviceid", new EntityReference("service", value.Value));
				else
					SetAttributeValue("preferredserviceid", value);
			}
		}

		public string ServiciopreferidoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredserviceid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ServiciopreferidoLabels { get; set; }

		/// <summary>
		///  
		/// 'PreferredSystemUserId'.<br />
		/// Elija el representante de servicio preferido para tenerlo como referencia cuando programe actividades de servicio para la cuenta.
		/// </summary>
		[AttributeLogicalName("preferredsystemuserid")]
		public Guid? Usuariopreferido
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredsystemuserid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredsystemuserid", new EntityReference("systemuser", value.Value));
				else
					SetAttributeValue("preferredsystemuserid", value);
			}
		}

		public EntityReference UsuariopreferidoReference => Usuariopreferido == null ? null : GetAttributeValue<EntityReference>("preferredsystemuserid");

		public string UsuariopreferidoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredsystemuserid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopreferidoLabels { get; set; }

		/// <summary>
		///  
		/// 'PrimaryContactId'.<br />
		/// Elija el contacto principal de la cuenta para facilitar el acceso rápido a los detalles del contacto.
		/// </summary>
		[AttributeLogicalName("primarycontactid")]
		public Guid? DIrectordelaescuela
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("primarycontactid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("primarycontactid", new EntityReference("contact", value.Value));
				else
					SetAttributeValue("primarycontactid", value);
			}
		}

		public string DIrectordelaescuelaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("primarycontactid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> DIrectordelaescuelaLabels { get; set; }

		/// <summary>
		/// [MaxLength=200] 
		/// 'PrimarySatoriId'.<br />
		/// Id. de Satori principal de la cuenta
		/// </summary>
		[AttributeLogicalName("primarysatoriid"), MaxLength(200), StringLength(200)]
		public string IddeSatoriprincipal
		{
			get
			{
				var value = GetAttributeValue<string>("primarysatoriid");
				return value;
			}
			set
			{
				SetAttributeValue("primarysatoriid", value);
			}
		}

		/// <summary>
		/// [MaxLength=128] 
		/// 'PrimaryTwitterId'.<br />
		/// Id. de Twitter principal de la cuenta
		/// </summary>
		[AttributeLogicalName("primarytwitterid"), MaxLength(128), StringLength(128)]
		public string IddeTwitterprincipal
		{
			get
			{
				var value = GetAttributeValue<string>("primarytwitterid");
				return value;
			}
			set
			{
				SetAttributeValue("primarytwitterid", value);
			}
		}

		/// <summary>
		///  
		/// 'ProcessId'.<br />
		/// Muestra el identificador del proceso.
		/// </summary>
		[AttributeLogicalName("processid")]
		public Guid? Proceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("processid");
				return value;
			}
			set
			{
				SetAttributeValue("processid", value);
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Revenue'.<br />
		/// Escriba los ingresos anuales para la cuenta, que servirán de indicadores en análisis de rendimiento financiero.
		/// </summary>
		[AttributeLogicalName("revenue"), Range(0, 100000000000000)]
		public decimal? Ingresosanuales
		{
			get
			{
				var value = GetAttributeValue<Money>("revenue");
				return value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("revenue", new Money(value.Value));
				else
					SetAttributeValue("revenue", value);
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Revenue_Base'.<br />
		/// Muestra los ingresos anuales convertidos a la divisa base predeterminada del sistema. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("revenue_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Ingresosanualesbase
		{
			get
			{
				var value = GetAttributeValue<Money>("revenue_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(0, 1000000000)] 
		/// 'SharesOutstanding'.<br />
		/// Escriba el número de acciones de la cuenta disponibles para el público. Este número servirá de indicador en análisis de rendimiento financiero.
		/// </summary>
		[AttributeLogicalName("sharesoutstanding"), Range(0, 1000000000)]
		public int? Valorespendientes
		{
			get
			{
				var value = GetAttributeValue<int?>("sharesoutstanding");
				return value;
			}
			set
			{
				SetAttributeValue("sharesoutstanding", value);
			}
		}

		/// <summary>
		///  
		/// 'ShippingMethodCode'.<br />
		/// Seleccione un método de envío para entregas enviadas a la dirección de la cuenta para designar el transportista preferido u otra opción de entrega.
		/// </summary>
		[AttributeLogicalName("shippingmethodcode")]
		public MododeenvioEnum? Mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("shippingmethodcode");
				return (MododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> MododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'SIC'.<br />
		/// Escriba el código de clasificación industrial estándar (SIC) que indica el sector principal de negocio de la cuenta, para su uso en segmentación de mercados y análisis demográficos.
		/// </summary>
		[AttributeLogicalName("sic"), MaxLength(20), StringLength(20)]
		public string CodigoSIC
		{
			get
			{
				var value = GetAttributeValue<string>("sic");
				return value;
			}
			set
			{
				SetAttributeValue("sic", value);
			}
		}

		/// <summary>
		///  
		/// 'SLAId'.<br />
		/// Elija el contrato de nivel de servicio (SLA) que desea aplicar al registro de cuenta.
		/// </summary>
		[AttributeLogicalName("slaid")]
		public Guid? SLA
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("slaid", new EntityReference("sla", value.Value));
				else
					SetAttributeValue("slaid", value);
			}
		}

		public string SLAName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> SLALabels { get; set; }

		/// <summary>
		///  
		/// 'SLAInvokedId'.<br />
		/// Último SLA que se aplicó a este caso. Este campo es solo para uso interno.
		/// </summary>
		[AttributeLogicalName("slainvokedid")]
		public Guid? UltimoSLAaplicado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Id;
			}
		}

		public string UltimoSLAaplicadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UltimoSLAaplicadoLabels { get; set; }

		/// <summary>
		///  
		/// 'StageId'.<br />
		/// Muestra el identificador de la fase.
		/// </summary>
		[AttributeLogicalName("stageid")]
		public Guid? __ObsoletoFasedeproceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("stageid");
				return value;
			}
			set
			{
				SetAttributeValue("stageid", value);
			}
		}

		/// <summary>
		///  
		/// 'StateCode'.<br />
		/// Muestra si la cuenta está activa o inactiva. Las cuentas inactivas son de solo lectura y no se pueden editar si no se reactivan.
		/// </summary>
		[AttributeLogicalName("statecode")]
		public EstadoEnum? Estado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				return (EstadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statecode", value);
			}
		}

		public IDictionary<int, string> EstadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'StatusCode'.<br />
		/// Seleccione el estado de la cuenta.
		/// </summary>
		[AttributeLogicalName("statuscode")]
		public RazonparaelestadoEnum? Razonparaelestado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				return (RazonparaelestadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statuscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statuscode", value);
			}
		}

		public IDictionary<int, string> RazonparaelestadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statuscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'StockExchange'.<br />
		/// Escriba el mercado de valores donde cotiza la cuenta para realizar un seguimiento de sus acciones y del rendimiento financiero de la compañía.
		/// </summary>
		[AttributeLogicalName("stockexchange"), MaxLength(20), StringLength(20)]
		public string Mercadodevalores
		{
			get
			{
				var value = GetAttributeValue<string>("stockexchange");
				return value;
			}
			set
			{
				SetAttributeValue("stockexchange", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'TeamsFollowed'.<br />
		/// Número de usuarios o conversaciones que siguieron el registro
		/// </summary>
		[AttributeLogicalName("teamsfollowed"), Range(-2147483648, 2147483647)]
		public int? Equiposseguidos
		{
			get
			{
				var value = GetAttributeValue<int?>("teamsfollowed");
				return value;
			}
			set
			{
				SetAttributeValue("teamsfollowed", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone1'.<br />
		/// Escriba el número de teléfono principal para esta cuenta.
		/// </summary>
		[AttributeLogicalName("telephone1"), MaxLength(50), StringLength(50)]
		public string Numerodecentralita
		{
			get
			{
				var value = GetAttributeValue<string>("telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone2'.<br />
		/// Escriba un segundo número de teléfono para esta cuenta.
		/// </summary>
		[AttributeLogicalName("telephone2"), MaxLength(50), StringLength(50)]
		public string Otrotelefono
		{
			get
			{
				var value = GetAttributeValue<string>("telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone3'.<br />
		/// Escriba un tercer número de teléfono para esta cuenta.
		/// </summary>
		[AttributeLogicalName("telephone3"), MaxLength(50), StringLength(50)]
		public string Telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("telephone3", value);
			}
		}

		/// <summary>
		///  
		/// 'TerritoryCode'.<br />
		/// Seleccione una región o un territorio de la cuenta para su uso en segmentación y análisis.
		/// </summary>
		[AttributeLogicalName("territorycode")]
		public CodigodezonadeventasEnum? Codigodezonadeventas
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("territorycode");
				return (CodigodezonadeventasEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("territorycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("territorycode", value);
			}
		}

		public IDictionary<int, string> CodigodezonadeventasLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("territorycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("territorycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'TerritoryId'.<br />
		/// Elija la región o la zona de ventas de la cuenta para asegurarse de que la cuenta se asigna al representante correcto y para su uso en segmentación y análisis.
		/// </summary>
		[AttributeLogicalName("territoryid")]
		public Guid? Zonadeventas
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("territoryid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("territoryid", new EntityReference("territory", value.Value));
				else
					SetAttributeValue("territoryid", value);
			}
		}

		public string ZonadeventasName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("territoryid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ZonadeventasLabels { get; set; }

		/// <summary>
		/// [MaxLength=10] 
		/// 'TickerSymbol'.<br />
		/// Escriba el símbolo del mercado de valores de la cuenta para realizar un seguimiento del rendimiento financiero de la compañía. Puede hacer clic en el código introducido en este campo para obtener acceso a la información de cotización más reciente de MSN Money.
		/// </summary>
		[AttributeLogicalName("tickersymbol"), MaxLength(10), StringLength(10)]
		public string Simbolodelvalor
		{
			get
			{
				var value = GetAttributeValue<string>("tickersymbol");
				return value;
			}
			set
			{
				SetAttributeValue("tickersymbol", value);
			}
		}

		/// <summary>
		/// [MaxLength=1250] 
		/// 'TimeSpentByMeOnEmailAndMeetings'.<br />
		/// Tiempo total que dedico a correos (leer y escribir) y reuniones relacionados con el registro de cuenta.
		/// </summary>
		[AttributeLogicalName("timespentbymeonemailandmeetings"), MaxLength(1250), StringLength(1250)]
		public string Tiempodedicadopormi
		{
			get
			{
				var value = GetAttributeValue<string>("timespentbymeonemailandmeetings");
				return value;
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'TimeZoneRuleVersionNumber'.<br />
		/// Para uso interno.
		/// </summary>
		[AttributeLogicalName("timezoneruleversionnumber"), Range(-1, 2147483647)]
		public int? Numerodeversionderegladezonahoraria
		{
			get
			{
				var value = GetAttributeValue<int?>("timezoneruleversionnumber");
				return value;
			}
			set
			{
				SetAttributeValue("timezoneruleversionnumber", value);
			}
		}

		/// <summary>
		///  
		/// 'TransactionCurrencyId'.<br />
		/// Elija la divisa local del registro para asegurarse de que en los presupuestos se utiliza la divisa correcta.
		/// </summary>
		[AttributeLogicalName("transactioncurrencyid")]
		public Guid? Divisa
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("transactioncurrencyid", new EntityReference("transactioncurrency", value.Value));
				else
					SetAttributeValue("transactioncurrencyid", value);
			}
		}

		public string DivisaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> DivisaLabels { get; set; }

		/// <summary>
		/// [MaxLength=1250] 
		/// 'TraversedPath'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("traversedpath"), MaxLength(1250), StringLength(1250)]
		public string __ObsoletoRutarecorrida
		{
			get
			{
				var value = GetAttributeValue<string>("traversedpath");
				return value;
			}
			set
			{
				SetAttributeValue("traversedpath", value);
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'UTCConversionTimeZoneCode'.<br />
		/// Código de la zona horaria que estaba en uso cuando se creó el registro.
		/// </summary>
		[AttributeLogicalName("utcconversiontimezonecode"), Range(-1, 2147483647)]
		public int? CodigodezonahorariadeconversionUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("utcconversiontimezonecode");
				return value;
			}
			set
			{
				SetAttributeValue("utcconversiontimezonecode", value);
			}
		}

		/// <summary>
		///  
		/// 'VersionNumber'.<br />
		/// Número de versión de la cuenta.
		/// </summary>
		[AttributeLogicalName("versionnumber")]
		public long? Numerodeversion
		{
			get
			{
				var value = GetAttributeValue<long?>("versionnumber");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'WebSiteURL'.<br />
		/// Escriba la dirección URL del sitio web de la cuenta para obtener detalles rápidos sobre el perfil de la compañía.
		/// </summary>
		[AttributeLogicalName("websiteurl"), MaxLength(200), StringLength(200)]
		public string Sitioweb
		{
			get
			{
				var value = GetAttributeValue<string>("websiteurl");
				return value;
			}
			set
			{
				SetAttributeValue("websiteurl", value);
			}
		}

		/// <summary>
		/// [MaxLength=160] 
		/// 'YomiName'.<br />
		/// Escriba la transcripción fonética del nombre de la compañía, si se especifica en japonés, para asegurarse de pronunciarlo correctamente en llamadas de teléfono y otras comunicaciones.
		/// </summary>
		[AttributeLogicalName("yominame"), MaxLength(160), StringLength(160)]
		public string NombredecuentaYomi
		{
			get
			{
				var value = GetAttributeValue<string>("yominame");
				return value;
			}
			set
			{
				SetAttributeValue("yominame", value);
			}
		}

		#endregion

		#region Relationships


		public static class RelationNames
		{
		}

		public override IDictionary<string, object[]> RelationProperties
		{
			get
			{
				if (relationProperties != null) return relationProperties;
				relationProperties = new Dictionary<string, object[]>();
				return relationProperties;
			}
		}

		#endregion

		/// <inheritdoc/>
		public Escuela(object obj) : base(obj, EntityLogicalName)
		{
			foreach (var p in obj.GetType().GetProperties())
			{
				var value = p.GetValue(obj, null);
				if (p.PropertyType == typeof(Guid))
				{
					base.Id = (Guid)value;
					Attributes["accountid"] = base.Id;
				}
				else if (p.Name == "FormattedValues")
				{
					FormattedValues.AddRange((FormattedValueCollection)value);
				}
				else
				{
					Attributes[p.Name.ToLower()] = value;
				}
			}
		}

		#region Label/value pairs

		public enum CategoriaEnum
		{
			Clientepreferido = 1,
			Estandar = 2,
		}

		public enum ClasificacionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum NiveldecuentaEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion1tipodedireccionEnum
		{
			Facturacion = 1,
			Envio = 2,
			Primario = 3,
			Otros = 4,
		}

		public enum Direccion1condicionesdefleteEnum
		{
			CFRCosteyflete = 3,
			CIFCosteseguroyflete = 4,
			CIPFleteysegurospagadoshasta = 5,
			CPTFletepagadohasta = 6,
			DAFFrancoenfrontera = 7,
			DEQFrancosobremuelle = 8,
			DESFrancoexship = 9,
			DDPFrancodespachadoenaduanas = 10,
			DDUFrancosindespacharenaduanas = 11,
			EXWEnfabrica = 12,
			FASFrancocostadobuque = 13,
			FCAFrancotransportista = 14,
			Otros = 15,
			EXQSobremuelle = 16,
			EXSExship = 17,
			FOAFrancoaeropuerto = 18,
			FORFrancovagon = 19,
			FRCFrancotransportista = 20,
			DCPelvendedorcorrecontodoslosgastoshastallegaraldestinoacordadolosriesgossetransfierenalcompradorcuandoseentregalamercanciaalprimertransportistaenelpuntodedestino = 23,
			FONFrancoabordo = 27,
		}

		public enum Direccion1mododeenvioEnum
		{
			Aereo = 1,
			DHL = 2,
			UPS = 4,
			CorreoPostal = 5,
			Cargacompleta = 6,
			Recogidaacargodelcliente = 7,
			Carretera = 9,
			Ferrocarril = 10,
			Maritimo = 11,
			Urgente = 39,
			SEUR = 40,
			MRW = 41,
			NACEX = 42,
			Grupaje = 43,
			Servicioadomicilio = 44,
		}

		public enum Direccion2tipodedireccionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion2condicionesdefleteEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion2mododeenvioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum TipodenegocioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum SuspensiondecreditoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum TamanodelclienteEnum
		{
			Valorpredeterminado = 1,
		}

		public enum TipoderelacionEnum
		{
			Competidor = 1,
			Consultor = 2,
			Cliente = 3,
			Inversor = 4,
			Asociado = 5,
			Personaconinfluencia = 6,
			Presionar = 7,
			Clientepotencial = 8,
			Revendedor = 9,
			Distribuidor = 10,
			Proveedor = 11,
			Otros = 12,
		}

		public enum NopermitircorreoelecenmasaEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitircorreoenmasaEnum
		{
			Si = 1,
			No = 0,
		}

		public enum NopermitircorreoelectronicoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitirfaxesEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitirllamadasdetelefonoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitircorreoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum EnviarmaterialesdemarketingEnum
		{
			Noenviar = 1,
			Enviar = 0,
		}

		public enum SeguiractividaddecorreoEnum
		{
			Permitir = 1,
			Nopermitir = 0,
		}

		public enum SectorEnum
		{
			Agriculturacazasilviculturaypesca = 34,
			Energiayagua = 35,
			Extraccionytransformaciondemineralesnoenergeticosyproductosderivadosindustriaquimica = 36,
			Industriasdetransformaciondelosmetalesmecanicadeprecision = 37,
			Otrasindustriasmanufactureras = 38,
			Construccioneingenieriacivil = 39,
			Comerciorestaurantesyhosteleriareparaciones = 40,
			Transportesycomunicaciones = 41,
			Institucionesfinancierassegurosserviciosprestadosalasempresasalquileres = 42,
			Otrosservicios = 43,
		}

		public enum SoloparamarketingEnum
		{
			Si = 1,
			No = 0,
		}

		public enum CombinadoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum PropiedadEnum
		{
			Publica = 1,
			Privada = 2,
			Subsidiaria = 3,
			Otros = 4,
		}

		public enum ParticipaenflujodetrabajoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum CondicionesdepagoEnum
		{
			Pagoa30dias = 1,
			_210pagoa30dias = 2,
			Pagoa45dias = 3,
			Pagoa60dias = 4,
			Pagoalcontado = 10,
			_306090 = 38,
			Aplazado = 39,
		}

		public enum DiapreferidoEnum
		{
			Domingo = 0,
			Lunes = 1,
			Martes = 2,
			Miercoles = 3,
			Jueves = 4,
			Viernes = 5,
			Sabado = 6,
		}

		public enum HorapreferidaEnum
		{
			Manana = 1,
			Tarde = 2,
			Ultimahora = 3,
		}

		public enum MetododecontactopreferidoEnum
		{
			Cualquiera = 1,
			Correoelectronico = 2,
			Telefono = 3,
			Fax = 4,
			Correo = 5,
		}

		public enum MododeenvioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum EstadoEnum
		{
			Activo = 0,
			Inactivo = 1,
		}

		public enum RazonparaelestadoEnum
		{
			Activo = 1,
			Inactivo = 2,
		}

		public enum CodigodezonadeventasEnum
		{
			Valorpredeterminado = 1,
		}

		#endregion

		#region Metadata

		#region Enums

		public static class Enums
		{
			/// <summary>
			/// Gets the label corresponding to the option-set's value using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="constant">The value from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns></returns>
			public static string GetLabel(string logicalName, int constant, int languageCode = 1033)
			{
				return GeneratorHelpers.GetLabel(logicalName, constant, typeof(Enums), languageCode);
			}
			/// <summary>
			/// Gets the value corresponding to the option-set's label using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="label">The label from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns>The value corresponding to the label</returns>
			public static int GetValue(string logicalName, string label, int languageCode = 1033)
			{
				return GeneratorHelpers.GetValue(logicalName, label, typeof(Enums), languageCode);
			}
		}

		#endregion

		#region Fields

		public static class Fields
		{
			#region Logical names

			public const string Categoria = "accountcategorycode";
			public const string Clasificacion = "accountclassificationcode";
			public const string CuentaId = "accountid";
			public const string Numerodecuenta = "accountnumber";
			public const string Niveldecuenta = "accountratingcode";
			public const string Direccion1tipodedireccion = "address1_addresstypecode";
			public const string Direccion1ciudad = "address1_city";
			public const string Direccion1 = "address1_composite";
			public const string Direccion1paisoregion = "address1_country";
			public const string Direccion1condado = "address1_county";
			public const string Direccion1fax = "address1_fax";
			public const string Direccion1condicionesdeflete = "address1_freighttermscode";
			public const string Direccion1latitud = "address1_latitude";
			public const string Direccion1calle1 = "address1_line1";
			public const string Direccion1calle2 = "address1_line2";
			public const string Direccion1calle3 = "address1_line3";
			public const string Direccion1longitud = "address1_longitude";
			public const string Direccion1nombre = "address1_name";
			public const string Direccion1codigopostal = "address1_postalcode";
			public const string Direccion1apartadodecorreos = "address1_postofficebox";
			public const string Direccion1nomcontactoppal = "address1_primarycontactname";
			public const string Direccion1mododeenvio = "address1_shippingmethodcode";
			public const string Direccion1estadooprovincia = "address1_stateorprovince";
			public const string DireccionTelefono = "address1_telephone1";
			public const string Direccion1telefono2 = "address1_telephone2";
			public const string Direccion1telefono3 = "address1_telephone3";
			public const string Direccion1zonadeUPS = "address1_upszone";
			public const string Direccion1desplazdeUTC = "address1_utcoffset";
			public const string Direccion2tipodedireccion = "address2_addresstypecode";
			public const string Direccion2ciudad = "address2_city";
			public const string Direccion2 = "address2_composite";
			public const string Direccion2paisoregion = "address2_country";
			public const string Direccion2condado = "address2_county";
			public const string Direccion2fax = "address2_fax";
			public const string Direccion2condicionesdeflete = "address2_freighttermscode";
			public const string Direccion2latitud = "address2_latitude";
			public const string Direccion2calle1 = "address2_line1";
			public const string Direccion2calle2 = "address2_line2";
			public const string Direccion2calle3 = "address2_line3";
			public const string Direccion2longitud = "address2_longitude";
			public const string Direccion2nombre = "address2_name";
			public const string Direccion2codigopostal = "address2_postalcode";
			public const string Direccion2apartadodecorreos = "address2_postofficebox";
			public const string Direccion2nomcontactoppal = "address2_primarycontactname";
			public const string Direccion2mododeenvio = "address2_shippingmethodcode";
			public const string Direccion2estadooprovincia = "address2_stateorprovince";
			public const string Direccion2telefono1 = "address2_telephone1";
			public const string Direccion2telefono2 = "address2_telephone2";
			public const string Direccion2telefono3 = "address2_telephone3";
			public const string Direccion2zonadeUPS = "address2_upszone";
			public const string Direccion2desplazdeUTC = "address2_utcoffset";
			public const string Vence30 = "aging30";
			public const string Vence30base = "aging30_base";
			public const string Vence60 = "aging60";
			public const string Vence60base = "aging60_base";
			public const string Vence90 = "aging90";
			public const string Vence90base = "aging90_base";
			public const string Tipodenegocio = "businesstypecode";
			public const string Autor = "createdby";
			public const string Creadoporparteexterna = "createdbyexternalparty";
			public const string Fechadecreacion = "createdon";
			public const string Autordelegado = "createdonbehalfby";
			public const string Limitedelcredito = "creditlimit";
			public const string Limitedecreditobase = "creditlimit_base";
			public const string Suspensiondecredito = "creditonhold";
			public const string Tamanodelcliente = "customersizecode";
			public const string Tipoderelacion = "customertypecode";
			public const string Listadeprecios = "defaultpricelevelid";
			public const string Descripcion = "description";
			public const string Nopermitircorreoelecenmasa = "donotbulkemail";
			public const string Nopermitircorreoenmasa = "donotbulkpostalmail";
			public const string Nopermitircorreoelectronico = "donotemail";
			public const string Nopermitirfaxes = "donotfax";
			public const string Nopermitirllamadasdetelefono = "donotphone";
			public const string Nopermitircorreo = "donotpostalmail";
			public const string Enviarmaterialesdemarketing = "donotsendmm";
			public const string Correoelectronico = "emailaddress1";
			public const string Direcciondecorreoelectronico2 = "emailaddress2";
			public const string Direcciondecorreoelectronico3 = "emailaddress3";
			public const string Imagenpredeterminada = "entityimage";
			public const string Identificadordeimagendelaentidad = "entityimageid";
			public const string Tipodecambio = "exchangerate";
			public const string Fax = "fax";
			public const string Seguiractividaddecorreo = "followemail";
			public const string SitiodeFTP = "ftpsiteurl";
			public const string Numerodesecuenciadeimportacion = "importsequencenumber";
			public const string Sector = "industrycode";
			public const string Ultimoperiododeretencion = "lastonholdtime";
			public const string Ultimodiaincluidoenlacampana = "lastusedincampaign";
			public const string Capitalizaciondemercado = "marketcap";
			public const string Capitalizaciondemercadobase = "marketcap_base";
			public const string Soloparamarketing = "marketingonly";
			public const string Idmaestro = "masterid";
			public const string Combinado = "merged";
			public const string Modificadopor = "modifiedby";
			public const string Modificadoporparteexterna = "modifiedbyexternalparty";
			public const string Fechademodificacion = "modifiedon";
			public const string Modificadopordelegado = "modifiedonbehalfby";
			public const string Nombredelaescuela = "name";
			public const string Identificador = "new_identifier";
			public const string NumAlumnos = "new_numalumnos";
			public const string NumAlumnosfechadeultimaactualizacion = "new_numalumnos_date";
			public const string NumAlumnosestado = "new_numalumnos_state";
			public const string NumProfesores = "new_numprofesores";
			public const string NumProfesoresfechadeultimaactualizacion = "new_numprofesores_date";
			public const string NumProfesoresestado = "new_numprofesores_state";
			public const string Numerodeempleados = "numberofemployees";
			public const string Periododeretencionminutos = "onholdtime";
			public const string Operacionesabiertas = "opendeals";
			public const string Operacionesabiertasfechadeultimaactualizacion = "opendeals_date";
			public const string Operacionesabiertasestado = "opendeals_state";
			public const string Ingresosabiertos = "openrevenue";
			public const string Ingresosabiertosbase = "openrevenue_base";
			public const string Ingresosabiertosfechadeultimaactualizacion = "openrevenue_date";
			public const string Ingresosabiertosestado = "openrevenue_state";
			public const string Clientepotencialoriginal = "originatingleadid";
			public const string Fechadecreaciondelregistro = "overriddencreatedon";
			public const string Propietario = "ownerid";
			public const string Propiedad = "ownershipcode";
			public const string Unidaddenegociopropietaria = "owningbusinessunit";
			public const string Equipopropietario = "owningteam";
			public const string Usuariopropietario = "owninguser";
			public const string Cuentaprimaria = "parentaccountid";
			public const string Participaenflujodetrabajo = "participatesinworkflow";
			public const string Condicionesdepago = "paymenttermscode";
			public const string Diapreferido = "preferredappointmentdaycode";
			public const string Horapreferida = "preferredappointmenttimecode";
			public const string Metododecontactopreferido = "preferredcontactmethodcode";
			public const string Instalacequipampreferidos = "preferredequipmentid";
			public const string Serviciopreferido = "preferredserviceid";
			public const string Usuariopreferido = "preferredsystemuserid";
			public const string DIrectordelaescuela = "primarycontactid";
			public const string IddeSatoriprincipal = "primarysatoriid";
			public const string IddeTwitterprincipal = "primarytwitterid";
			public const string Proceso = "processid";
			public const string Ingresosanuales = "revenue";
			public const string Ingresosanualesbase = "revenue_base";
			public const string Valorespendientes = "sharesoutstanding";
			public const string Mododeenvio = "shippingmethodcode";
			public const string CodigoSIC = "sic";
			public const string SLA = "slaid";
			public const string UltimoSLAaplicado = "slainvokedid";
			public const string __ObsoletoFasedeproceso = "stageid";
			public const string Estado = "statecode";
			public const string Razonparaelestado = "statuscode";
			public const string Mercadodevalores = "stockexchange";
			public const string Equiposseguidos = "teamsfollowed";
			public const string Numerodecentralita = "telephone1";
			public const string Otrotelefono = "telephone2";
			public const string Telefono3 = "telephone3";
			public const string Codigodezonadeventas = "territorycode";
			public const string Zonadeventas = "territoryid";
			public const string Simbolodelvalor = "tickersymbol";
			public const string Tiempodedicadopormi = "timespentbymeonemailandmeetings";
			public const string Numerodeversionderegladezonahoraria = "timezoneruleversionnumber";
			public const string Divisa = "transactioncurrencyid";
			public const string __ObsoletoRutarecorrida = "traversedpath";
			public const string CodigodezonahorariadeconversionUTC = "utcconversiontimezonecode";
			public const string Numerodeversion = "versionnumber";
			public const string Sitioweb = "websiteurl";
			public const string NombredecuentaYomi = "yominame";

			#endregion
		}

		#endregion

		#endregion
	}

	#endregion

	#region Profesor

	/// <summary>
	/// 'Contact'.<br />
	/// Persona con la que tiene una relación una unidad de negocio, como por ejemplo, un cliente, un distribuidor o un colega.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract, EntityLogicalName("contact")]
	public partial class Profesor : GeneratedEntity<Profesor.RelationName>
	{
		public Profesor() : base(EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Profesor(string[] keys, object[] values) : base(keys, values, EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Profesor(object obj, Type limitingType) : base(obj, limitingType, EntityLogicalName)
		{ }

		public const string DisplayName = "Profesor";
		public const string SchemaName = "Contact";
		public const string EntityLogicalName = "contact";
		public const int EntityTypeCode = 2;

		public class RelationName : RelationNameBase
		{
			public RelationName(string name) : base(name)
			{ }
		}

		#region Attributes

		[AttributeLogicalName("contactid")]
		public override System.Guid Id
		{
			get => (ContactoId == null || ContactoId == Guid.Empty) ? base.Id : ContactoId.GetValueOrDefault();
			set
			{
				if (value == Guid.Empty)
				{
					Attributes.Remove("contactid");
					base.Id = value;
				}
				else
				{
					ContactoId = value;
				}
			}
		}

		/// <summary>
		///  
		/// 'AccountId'.<br />
		/// Identificador único de la cuenta con la que está asociado el contacto.
		/// </summary>
		[AttributeLogicalName("accountid")]
		public Guid? Cuenta
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("accountid");
				return value?.Id;
			}
		}

		public EntityReference CuentaReference => Cuenta == null ? null : GetAttributeValue<EntityReference>("accountid");

		public string CuentaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("accountid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> CuentaLabels { get; set; }

		/// <summary>
		///  
		/// 'AccountRoleCode'.<br />
		/// Seleccione el rol del contacto en la compañía o el proceso de ventas, como responsable de toma de decisiones, empleado o persona con influencia.
		/// </summary>
		[AttributeLogicalName("accountrolecode")]
		public RolEnum? Rol
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountrolecode");
				return (RolEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("accountrolecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("accountrolecode", value);
			}
		}

		public IDictionary<int, string> RolLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("accountrolecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("accountrolecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'Address1_AddressTypeCode'.<br />
		/// Seleccione el tipo de dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_addresstypecode")]
		public Direccion1tipodedireccionEnum? Direccion1tipodedireccion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_addresstypecode");
				return (Direccion1tipodedireccionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_addresstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_addresstypecode", value);
			}
		}

		public IDictionary<int, string> Direccion1tipodedireccionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_addresstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_addresstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address1_City'.<br />
		/// Escriba la ciudad de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_city"), MaxLength(80), StringLength(80)]
		public string Direccion1ciudad
		{
			get
			{
				var value = GetAttributeValue<string>("address1_city");
				return value;
			}
			set
			{
				SetAttributeValue("address1_city", value);
			}
		}

		/// <summary>
		/// [MaxLength=1000] 
		/// 'Address1_Composite'.<br />
		/// Muestra la dirección principal completa.
		/// </summary>
		[AttributeLogicalName("address1_composite"), MaxLength(1000), StringLength(1000)]
		public string Direccion1
		{
			get
			{
				var value = GetAttributeValue<string>("address1_composite");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address1_Country'.<br />
		/// Escriba el país o la región de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_country"), MaxLength(80), StringLength(80)]
		public string Direccion1paisoregion
		{
			get
			{
				var value = GetAttributeValue<string>("address1_country");
				return value;
			}
			set
			{
				SetAttributeValue("address1_country", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_County'.<br />
		/// Escriba el condado de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_county"), MaxLength(50), StringLength(50)]
		public string Direccion1condado
		{
			get
			{
				var value = GetAttributeValue<string>("address1_county");
				return value;
			}
			set
			{
				SetAttributeValue("address1_county", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Fax'.<br />
		/// Escriba el número de fax asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_fax"), MaxLength(50), StringLength(50)]
		public string Direccion1fax
		{
			get
			{
				var value = GetAttributeValue<string>("address1_fax");
				return value;
			}
			set
			{
				SetAttributeValue("address1_fax", value);
			}
		}

		/// <summary>
		///  
		/// 'Address1_FreightTermsCode'.<br />
		/// Seleccione las condiciones de flete de la dirección principal para asegurarse de que los pedidos de envío se procesan correctamente.
		/// </summary>
		[AttributeLogicalName("address1_freighttermscode")]
		public Direccion1condicionesdefleteEnum? Direccion1condicionesdeflete
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_freighttermscode");
				return (Direccion1condicionesdefleteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_freighttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_freighttermscode", value);
			}
		}

		public IDictionary<int, string> Direccion1condicionesdefleteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_freighttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_freighttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-90, 90)] 
		/// 'Address1_Latitude'.<br />
		/// Escriba el valor de latitud de la dirección principal para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address1_latitude"), Range(-90, 90)]
		public double? Direccion1latitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address1_latitude");
				return value;
			}
			set
			{
				SetAttributeValue("address1_latitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line1'.<br />
		/// Escriba la primera línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line1"), MaxLength(250), StringLength(250)]
		public string Direccion1calle1
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line1");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line1", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line2'.<br />
		/// Escriba la segunda línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line2"), MaxLength(250), StringLength(250)]
		public string Direccion1calle2
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line2");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line2", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address1_Line3'.<br />
		/// Escriba la tercera línea de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_line3"), MaxLength(250), StringLength(250)]
		public string Direccion1calle3
		{
			get
			{
				var value = GetAttributeValue<string>("address1_line3");
				return value;
			}
			set
			{
				SetAttributeValue("address1_line3", value);
			}
		}

		/// <summary>
		/// [Range(-180, 180)] 
		/// 'Address1_Longitude'.<br />
		/// Escriba el valor de longitud de la dirección principal para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address1_longitude"), Range(-180, 180)]
		public double? Direccion1longitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address1_longitude");
				return value;
			}
			set
			{
				SetAttributeValue("address1_longitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'Address1_Name'.<br />
		/// Escriba un nombre descriptivo para la dirección principal, como Oficinas centrales.
		/// </summary>
		[AttributeLogicalName("address1_name"), MaxLength(200), StringLength(200)]
		public string Direccion1nombre
		{
			get
			{
				var value = GetAttributeValue<string>("address1_name");
				return value;
			}
			set
			{
				SetAttributeValue("address1_name", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address1_PostalCode'.<br />
		/// Escriba el código postal de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_postalcode"), MaxLength(20), StringLength(20)]
		public string Direccion1codigopostal
		{
			get
			{
				var value = GetAttributeValue<string>("address1_postalcode");
				return value;
			}
			set
			{
				SetAttributeValue("address1_postalcode", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address1_PostOfficeBox'.<br />
		/// Escriba el número de apartado de correos de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_postofficebox"), MaxLength(20), StringLength(20)]
		public string Direccion1apartadodecorreos
		{
			get
			{
				var value = GetAttributeValue<string>("address1_postofficebox");
				return value;
			}
			set
			{
				SetAttributeValue("address1_postofficebox", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address1_PrimaryContactName'.<br />
		/// Escriba el nombre del contacto principal en la dirección principal de la cuenta.
		/// </summary>
		[AttributeLogicalName("address1_primarycontactname"), MaxLength(100), StringLength(100)]
		public string Direccion1nomcontactoppal
		{
			get
			{
				var value = GetAttributeValue<string>("address1_primarycontactname");
				return value;
			}
			set
			{
				SetAttributeValue("address1_primarycontactname", value);
			}
		}

		/// <summary>
		///  
		/// 'Address1_ShippingMethodCode'.<br />
		/// Seleccione un método de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("address1_shippingmethodcode")]
		public Direccion1mododeenvioEnum? Direccion1mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_shippingmethodcode");
				return (Direccion1mododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address1_shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address1_shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> Direccion1mododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address1_shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address1_shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_StateOrProvince'.<br />
		/// Escriba el estado o la provincia de la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_stateorprovince"), MaxLength(50), StringLength(50)]
		public string Direccion1estadooprovincia
		{
			get
			{
				var value = GetAttributeValue<string>("address1_stateorprovince");
				return value;
			}
			set
			{
				SetAttributeValue("address1_stateorprovince", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone1'.<br />
		/// Escriba el número de teléfono principal asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone1"), MaxLength(50), StringLength(50)]
		public string Direccion1telefono
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone2'.<br />
		/// Escriba un segundo número de teléfono asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone2"), MaxLength(50), StringLength(50)]
		public string Direccion1telefono2
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address1_Telephone3'.<br />
		/// Escriba un tercer número de teléfono asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address1_telephone3"), MaxLength(50), StringLength(50)]
		public string Direccion1telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("address1_telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("address1_telephone3", value);
			}
		}

		/// <summary>
		/// [MaxLength=4] 
		/// 'Address1_UPSZone'.<br />
		/// Escriba la zona de UPS de la dirección principal para asegurarse de que los costes de envío se calculan correctamente y las entregas se realizan puntualmente si se expiden a través de UPS.
		/// </summary>
		[AttributeLogicalName("address1_upszone"), MaxLength(4), StringLength(4)]
		public string Direccion1zonadeUPS
		{
			get
			{
				var value = GetAttributeValue<string>("address1_upszone");
				return value;
			}
			set
			{
				SetAttributeValue("address1_upszone", value);
			}
		}

		/// <summary>
		/// [Range(-1500, 1500)] 
		/// 'Address1_UTCOffset'.<br />
		/// Seleccione la zona horaria, o desplazamiento de UTC, para esta dirección de modo que otras personas puedan consultarla cuando se pongan en contacto con alguien en esta dirección.
		/// </summary>
		[AttributeLogicalName("address1_utcoffset"), Range(-1500, 1500)]
		public int? Direccion1desplazdeUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("address1_utcoffset");
				return value;
			}
			set
			{
				SetAttributeValue("address1_utcoffset", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_AddressTypeCode'.<br />
		/// Seleccione el tipo de dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_addresstypecode")]
		public Direccion2tipodedireccionEnum? Direccion2tipodedireccion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_addresstypecode");
				return (Direccion2tipodedireccionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_addresstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_addresstypecode", value);
			}
		}

		public IDictionary<int, string> Direccion2tipodedireccionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_addresstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_addresstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address2_City'.<br />
		/// Escriba la ciudad de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_city"), MaxLength(80), StringLength(80)]
		public string Direccion2ciudad
		{
			get
			{
				var value = GetAttributeValue<string>("address2_city");
				return value;
			}
			set
			{
				SetAttributeValue("address2_city", value);
			}
		}

		/// <summary>
		/// [MaxLength=1000] 
		/// 'Address2_Composite'.<br />
		/// Muestra la dirección secundaria completa.
		/// </summary>
		[AttributeLogicalName("address2_composite"), MaxLength(1000), StringLength(1000)]
		public string Direccion2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_composite");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address2_Country'.<br />
		/// Escriba el país o la región de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_country"), MaxLength(80), StringLength(80)]
		public string Direccion2paisoregion
		{
			get
			{
				var value = GetAttributeValue<string>("address2_country");
				return value;
			}
			set
			{
				SetAttributeValue("address2_country", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_County'.<br />
		/// Escriba el condado de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_county"), MaxLength(50), StringLength(50)]
		public string Direccion2condado
		{
			get
			{
				var value = GetAttributeValue<string>("address2_county");
				return value;
			}
			set
			{
				SetAttributeValue("address2_county", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Fax'.<br />
		/// Escriba el número de fax asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_fax"), MaxLength(50), StringLength(50)]
		public string Direccion2fax
		{
			get
			{
				var value = GetAttributeValue<string>("address2_fax");
				return value;
			}
			set
			{
				SetAttributeValue("address2_fax", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_FreightTermsCode'.<br />
		/// Seleccione las condiciones de flete de la dirección secundaria para asegurarse de que los pedidos de envío se procesan correctamente.
		/// </summary>
		[AttributeLogicalName("address2_freighttermscode")]
		public Direccion2condicionesdefleteEnum? Direccion2condicionesdeflete
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_freighttermscode");
				return (Direccion2condicionesdefleteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_freighttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_freighttermscode", value);
			}
		}

		public IDictionary<int, string> Direccion2condicionesdefleteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_freighttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_freighttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-90, 90)] 
		/// 'Address2_Latitude'.<br />
		/// Escriba el valor de latitud de la dirección secundaria para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address2_latitude"), Range(-90, 90)]
		public double? Direccion2latitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address2_latitude");
				return value;
			}
			set
			{
				SetAttributeValue("address2_latitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line1'.<br />
		/// Escriba la primera línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line1"), MaxLength(250), StringLength(250)]
		public string Direccion2calle1
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line1");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line1", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line2'.<br />
		/// Escriba la segunda línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line2"), MaxLength(250), StringLength(250)]
		public string Direccion2calle2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line2");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line2", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address2_Line3'.<br />
		/// Escriba la tercera línea de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_line3"), MaxLength(250), StringLength(250)]
		public string Direccion2calle3
		{
			get
			{
				var value = GetAttributeValue<string>("address2_line3");
				return value;
			}
			set
			{
				SetAttributeValue("address2_line3", value);
			}
		}

		/// <summary>
		/// [Range(-180, 180)] 
		/// 'Address2_Longitude'.<br />
		/// Escriba el valor de longitud de la dirección secundaria para utilizar en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address2_longitude"), Range(-180, 180)]
		public double? Direccion2longitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address2_longitude");
				return value;
			}
			set
			{
				SetAttributeValue("address2_longitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address2_Name'.<br />
		/// Escriba un nombre descriptivo para la dirección secundaria, como Oficinas centrales.
		/// </summary>
		[AttributeLogicalName("address2_name"), MaxLength(100), StringLength(100)]
		public string Direccion2nombre
		{
			get
			{
				var value = GetAttributeValue<string>("address2_name");
				return value;
			}
			set
			{
				SetAttributeValue("address2_name", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address2_PostalCode'.<br />
		/// Escriba el código postal de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_postalcode"), MaxLength(20), StringLength(20)]
		public string Direccion2codigopostal
		{
			get
			{
				var value = GetAttributeValue<string>("address2_postalcode");
				return value;
			}
			set
			{
				SetAttributeValue("address2_postalcode", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address2_PostOfficeBox'.<br />
		/// Escriba el número de apartado de correos de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_postofficebox"), MaxLength(20), StringLength(20)]
		public string Direccion2apartadodecorreos
		{
			get
			{
				var value = GetAttributeValue<string>("address2_postofficebox");
				return value;
			}
			set
			{
				SetAttributeValue("address2_postofficebox", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address2_PrimaryContactName'.<br />
		/// Escriba el nombre del contacto principal en la dirección secundaria de la cuenta.
		/// </summary>
		[AttributeLogicalName("address2_primarycontactname"), MaxLength(100), StringLength(100)]
		public string Direccion2nomcontactoppal
		{
			get
			{
				var value = GetAttributeValue<string>("address2_primarycontactname");
				return value;
			}
			set
			{
				SetAttributeValue("address2_primarycontactname", value);
			}
		}

		/// <summary>
		///  
		/// 'Address2_ShippingMethodCode'.<br />
		/// Seleccione un método de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("address2_shippingmethodcode")]
		public Direccion2mododeenvioEnum? Direccion2mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_shippingmethodcode");
				return (Direccion2mododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address2_shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address2_shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> Direccion2mododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address2_shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address2_shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_StateOrProvince'.<br />
		/// Escriba el estado o la provincia de la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_stateorprovince"), MaxLength(50), StringLength(50)]
		public string Direccion2estadooprovincia
		{
			get
			{
				var value = GetAttributeValue<string>("address2_stateorprovince");
				return value;
			}
			set
			{
				SetAttributeValue("address2_stateorprovince", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone1'.<br />
		/// Escriba el número de teléfono principal asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone1"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono1
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone2'.<br />
		/// Escriba un segundo número de teléfono asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone2"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono2
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address2_Telephone3'.<br />
		/// Escriba un tercer número de teléfono asociado con la dirección secundaria.
		/// </summary>
		[AttributeLogicalName("address2_telephone3"), MaxLength(50), StringLength(50)]
		public string Direccion2telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("address2_telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("address2_telephone3", value);
			}
		}

		/// <summary>
		/// [MaxLength=4] 
		/// 'Address2_UPSZone'.<br />
		/// Escriba la zona de UPS de la dirección secundaria para asegurarse de que los costes de envío se calculan correctamente y las entregas se realizan puntualmente si se expiden a través de UPS.
		/// </summary>
		[AttributeLogicalName("address2_upszone"), MaxLength(4), StringLength(4)]
		public string Direccion2zonadeUPS
		{
			get
			{
				var value = GetAttributeValue<string>("address2_upszone");
				return value;
			}
			set
			{
				SetAttributeValue("address2_upszone", value);
			}
		}

		/// <summary>
		/// [Range(-1500, 1500)] 
		/// 'Address2_UTCOffset'.<br />
		/// Seleccione la zona horaria, o desplazamiento de UTC, para esta dirección de modo que otras personas puedan consultarla cuando se pongan en contacto con alguien en esta dirección.
		/// </summary>
		[AttributeLogicalName("address2_utcoffset"), Range(-1500, 1500)]
		public int? Direccion2desplazdeUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("address2_utcoffset");
				return value;
			}
			set
			{
				SetAttributeValue("address2_utcoffset", value);
			}
		}

		/// <summary>
		///  
		/// 'Address3_AddressTypeCode'.<br />
		/// Seleccione el tercer tipo de dirección.
		/// </summary>
		[AttributeLogicalName("address3_addresstypecode")]
		public Direccion3tipodedireccionEnum? Direccion3tipodedireccion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_addresstypecode");
				return (Direccion3tipodedireccionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address3_addresstypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address3_addresstypecode", value);
			}
		}

		public IDictionary<int, string> Direccion3tipodedireccionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_addresstypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address3_addresstypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address3_City'.<br />
		/// Escriba la ciudad de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_city"), MaxLength(80), StringLength(80)]
		public string Direccion3ciudad
		{
			get
			{
				var value = GetAttributeValue<string>("address3_city");
				return value;
			}
			set
			{
				SetAttributeValue("address3_city", value);
			}
		}

		/// <summary>
		/// [MaxLength=1000] 
		/// 'Address3_Composite'.<br />
		/// Muestra la tercera dirección completa.
		/// </summary>
		[AttributeLogicalName("address3_composite"), MaxLength(1000), StringLength(1000)]
		public string Direccion3
		{
			get
			{
				var value = GetAttributeValue<string>("address3_composite");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=80] 
		/// 'Address3_Country'.<br />
		/// el país o la región de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_country"), MaxLength(80), StringLength(80)]
		public string Direccion3paisoregion
		{
			get
			{
				var value = GetAttributeValue<string>("address3_country");
				return value;
			}
			set
			{
				SetAttributeValue("address3_country", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_County'.<br />
		/// Escriba el condado de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_county"), MaxLength(50), StringLength(50)]
		public string Direccion3condado
		{
			get
			{
				var value = GetAttributeValue<string>("address3_county");
				return value;
			}
			set
			{
				SetAttributeValue("address3_county", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_Fax'.<br />
		/// Escriba el número de fax asociado a la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_fax"), MaxLength(50), StringLength(50)]
		public string Direccion3fax
		{
			get
			{
				var value = GetAttributeValue<string>("address3_fax");
				return value;
			}
			set
			{
				SetAttributeValue("address3_fax", value);
			}
		}

		/// <summary>
		///  
		/// 'Address3_FreightTermsCode'.<br />
		/// Seleccione las condiciones de flete de la tercera dirección para asegurarse de que los pedidos de envío se procesan correctamente.
		/// </summary>
		[AttributeLogicalName("address3_freighttermscode")]
		public Direccion3condicionesdefleteEnum? Direccion3condicionesdeflete
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_freighttermscode");
				return (Direccion3condicionesdefleteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address3_freighttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address3_freighttermscode", value);
			}
		}

		public IDictionary<int, string> Direccion3condicionesdefleteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_freighttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address3_freighttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-90, 90)] 
		/// 'Address3_Latitude'.<br />
		/// Escriba el valor de latitud de la tercera dirección que se usará en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address3_latitude"), Range(-90, 90)]
		public double? Direccion3latitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address3_latitude");
				return value;
			}
			set
			{
				SetAttributeValue("address3_latitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address3_Line1'.<br />
		/// la primera línea de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_line1"), MaxLength(250), StringLength(250)]
		public string Direccion3calle1
		{
			get
			{
				var value = GetAttributeValue<string>("address3_line1");
				return value;
			}
			set
			{
				SetAttributeValue("address3_line1", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address3_Line2'.<br />
		/// la segunda línea de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_line2"), MaxLength(250), StringLength(250)]
		public string Direccion3calle2
		{
			get
			{
				var value = GetAttributeValue<string>("address3_line2");
				return value;
			}
			set
			{
				SetAttributeValue("address3_line2", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Address3_Line3'.<br />
		/// la tercera línea de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_line3"), MaxLength(250), StringLength(250)]
		public string Direccion3calle3
		{
			get
			{
				var value = GetAttributeValue<string>("address3_line3");
				return value;
			}
			set
			{
				SetAttributeValue("address3_line3", value);
			}
		}

		/// <summary>
		/// [Range(-180, 180)] 
		/// 'Address3_Longitude'.<br />
		/// Escriba el valor de longitud de la tercera dirección que se usará en la asignación y otras aplicaciones.
		/// </summary>
		[AttributeLogicalName("address3_longitude"), Range(-180, 180)]
		public double? Direccion3longitud
		{
			get
			{
				var value = GetAttributeValue<double?>("address3_longitude");
				return value;
			}
			set
			{
				SetAttributeValue("address3_longitude", value);
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'Address3_Name'.<br />
		/// Escriba un nombre descriptivo para la tercera dirección, como Oficinas centrales.
		/// </summary>
		[AttributeLogicalName("address3_name"), MaxLength(200), StringLength(200)]
		public string Direccion3nombre
		{
			get
			{
				var value = GetAttributeValue<string>("address3_name");
				return value;
			}
			set
			{
				SetAttributeValue("address3_name", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address3_PostalCode'.<br />
		/// el código postal de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_postalcode"), MaxLength(20), StringLength(20)]
		public string Direccion3codigopostal
		{
			get
			{
				var value = GetAttributeValue<string>("address3_postalcode");
				return value;
			}
			set
			{
				SetAttributeValue("address3_postalcode", value);
			}
		}

		/// <summary>
		/// [MaxLength=20] 
		/// 'Address3_PostOfficeBox'.<br />
		/// el número de apartado de correos de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_postofficebox"), MaxLength(20), StringLength(20)]
		public string Direccion3apartadodecorreos
		{
			get
			{
				var value = GetAttributeValue<string>("address3_postofficebox");
				return value;
			}
			set
			{
				SetAttributeValue("address3_postofficebox", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Address3_PrimaryContactName'.<br />
		/// Escriba el nombre del contacto principal en la tercera dirección de la cuenta.
		/// </summary>
		[AttributeLogicalName("address3_primarycontactname"), MaxLength(100), StringLength(100)]
		public string Direccion3nombredelcontactoprincipal
		{
			get
			{
				var value = GetAttributeValue<string>("address3_primarycontactname");
				return value;
			}
			set
			{
				SetAttributeValue("address3_primarycontactname", value);
			}
		}

		/// <summary>
		///  
		/// 'Address3_ShippingMethodCode'.<br />
		/// Seleccione un modo de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("address3_shippingmethodcode")]
		public Direccion3mododeenvioEnum? Direccion3mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_shippingmethodcode");
				return (Direccion3mododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("address3_shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("address3_shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> Direccion3mododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("address3_shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("address3_shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_StateOrProvince'.<br />
		/// el estado o la provincia de la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_stateorprovince"), MaxLength(50), StringLength(50)]
		public string Direccion3estadooprovincia
		{
			get
			{
				var value = GetAttributeValue<string>("address3_stateorprovince");
				return value;
			}
			set
			{
				SetAttributeValue("address3_stateorprovince", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_Telephone1'.<br />
		/// Escriba el número de teléfono principal asociado a la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_telephone1"), MaxLength(50), StringLength(50)]
		public string Direccion3telefono1
		{
			get
			{
				var value = GetAttributeValue<string>("address3_telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("address3_telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_Telephone2'.<br />
		/// Escriba un segundo número de teléfono asociado a la tercera dirección.
		/// </summary>
		[AttributeLogicalName("address3_telephone2"), MaxLength(50), StringLength(50)]
		public string Direccion3telefono2
		{
			get
			{
				var value = GetAttributeValue<string>("address3_telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("address3_telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Address3_Telephone3'.<br />
		/// Escriba un tercer número de teléfono asociado con la dirección principal.
		/// </summary>
		[AttributeLogicalName("address3_telephone3"), MaxLength(50), StringLength(50)]
		public string Direccion3telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("address3_telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("address3_telephone3", value);
			}
		}

		/// <summary>
		/// [MaxLength=4] 
		/// 'Address3_UPSZone'.<br />
		/// Escriba la zona de UPS de la tercera dirección para asegurarse de que los costes de envío se calculan correctamente y las entregas se realizan puntualmente si se expiden a través de UPS.
		/// </summary>
		[AttributeLogicalName("address3_upszone"), MaxLength(4), StringLength(4)]
		public string Direccion3zonadeUPS
		{
			get
			{
				var value = GetAttributeValue<string>("address3_upszone");
				return value;
			}
			set
			{
				SetAttributeValue("address3_upszone", value);
			}
		}

		/// <summary>
		/// [Range(-1500, 1500)] 
		/// 'Address3_UTCOffset'.<br />
		/// Seleccione la zona horaria, o desplazamiento de UTC, para esta dirección de modo que otras personas puedan consultarla cuando se pongan en contacto con alguien en esta dirección.
		/// </summary>
		[AttributeLogicalName("address3_utcoffset"), Range(-1500, 1500)]
		public int? Direccion3desplazamientodeUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("address3_utcoffset");
				return value;
			}
			set
			{
				SetAttributeValue("address3_utcoffset", value);
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging30'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging30"), Range(0, 100000000000000)]
		public decimal? Vence30
		{
			get
			{
				var value = GetAttributeValue<Money>("aging30");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging30_Base'.<br />
		/// Muestra el campo Vence 30 convertido a la divisa base predeterminada del sistema. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("aging30_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence30base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging30_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging60'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging60"), Range(0, 100000000000000)]
		public decimal? Vence60
		{
			get
			{
				var value = GetAttributeValue<Money>("aging60");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging60_Base'.<br />
		/// Muestra el campo Vence 60 convertido a la divisa base predeterminada del sistema. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("aging60_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence60base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging60_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'Aging90'.<br />
		/// Solo para uso del sistema.
		/// </summary>
		[AttributeLogicalName("aging90"), Range(0, 100000000000000)]
		public decimal? Vence90
		{
			get
			{
				var value = GetAttributeValue<Money>("aging90");
				return value?.Value;
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'Aging90_Base'.<br />
		/// Muestra el campo Vence 90 convertido a la divisa base predeterminada del sistema. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("aging90_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Vence90base
		{
			get
			{
				var value = GetAttributeValue<Money>("aging90_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'Anniversary'.<br />
		/// Escriba la fecha de la boda o del aniversario de servicio del contacto para utilizarla en programas de regalos u otras comunicaciones con clientes.
		/// </summary>
		[AttributeLogicalName("anniversary")]
		public DateTime? Aniversario
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("anniversary");
				return value;
			}
			set
			{
				SetAttributeValue("anniversary", value);
			}
		}

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'AnnualIncome'.<br />
		/// Escriba los ingresos anuales del contacto para utilizarlos en la elaboración de perfiles y análisis financieros.
		/// </summary>
		[AttributeLogicalName("annualincome"), Range(0, 100000000000000)]
		public decimal? Ingresosanuales
		{
			get
			{
				var value = GetAttributeValue<Money>("annualincome");
				return value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("annualincome", new Money(value.Value));
				else
					SetAttributeValue("annualincome", value);
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'AnnualIncome_Base'.<br />
		/// Muestra el campo Ingresos anuales convertido a la divisa base predeterminada del sistema. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("annualincome_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Ingresosanualesbase
		{
			get
			{
				var value = GetAttributeValue<Money>("annualincome_base");
				return value?.Value;
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'AssistantName'.<br />
		/// Escriba el nombre del ayudante del contacto.
		/// </summary>
		[AttributeLogicalName("assistantname"), MaxLength(100), StringLength(100)]
		public string Ayudante
		{
			get
			{
				var value = GetAttributeValue<string>("assistantname");
				return value;
			}
			set
			{
				SetAttributeValue("assistantname", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'AssistantPhone'.<br />
		/// Escriba el número de teléfono del ayudante del contacto.
		/// </summary>
		[AttributeLogicalName("assistantphone"), MaxLength(50), StringLength(50)]
		public string Telefonodelayudante
		{
			get
			{
				var value = GetAttributeValue<string>("assistantphone");
				return value;
			}
			set
			{
				SetAttributeValue("assistantphone", value);
			}
		}

		/// <summary>
		///  
		/// 'BirthDate'.<br />
		/// Escriba el cumpleaños del contacto para utilizarlo en programas de regalos u otras comunicaciones con clientes.
		/// </summary>
		[AttributeLogicalName("birthdate")]
		public DateTime? Cumpleanos
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("birthdate");
				return value;
			}
			set
			{
				SetAttributeValue("birthdate", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Business2'.<br />
		/// Escriba un segundo número de teléfono del trabajo para este contacto.
		/// </summary>
		[AttributeLogicalName("business2"), MaxLength(50), StringLength(50)]
		public string Telefonodeltrabajo2
		{
			get
			{
				var value = GetAttributeValue<string>("business2");
				return value;
			}
			set
			{
				SetAttributeValue("business2", value);
			}
		}

		/// <summary>
		/// [MaxLength=1073741823] 
		/// 'BusinessCard'.<br />
		/// Almacena una imagen de la tarjeta de presentación
		/// </summary>
		[AttributeLogicalName("businesscard"), MaxLength(1073741823), StringLength(1073741823)]
		public string Tarjetadepresentacion
		{
			get
			{
				var value = GetAttributeValue<string>("businesscard");
				return value;
			}
			set
			{
				SetAttributeValue("businesscard", value);
			}
		}

		/// <summary>
		/// [MaxLength=4000] 
		/// 'BusinessCardAttributes'.<br />
		/// Almacena las propiedades del control de tarjeta de presentación.
		/// </summary>
		[AttributeLogicalName("businesscardattributes"), MaxLength(4000), StringLength(4000)]
		public string BusinessCardAttributes
		{
			get
			{
				var value = GetAttributeValue<string>("businesscardattributes");
				return value;
			}
			set
			{
				SetAttributeValue("businesscardattributes", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Callback'.<br />
		/// Escriba un número de teléfono de devolución de llamada para este contacto.
		/// </summary>
		[AttributeLogicalName("callback"), MaxLength(50), StringLength(50)]
		public string Numerodedevoluciondellamada
		{
			get
			{
				var value = GetAttributeValue<string>("callback");
				return value;
			}
			set
			{
				SetAttributeValue("callback", value);
			}
		}

		/// <summary>
		/// [MaxLength=255] 
		/// 'ChildrensNames'.<br />
		/// Escriba los nombres de los hijos del contacto para tenerlos como referencia en comunicaciones y programas de cliente.
		/// </summary>
		[AttributeLogicalName("childrensnames"), MaxLength(255), StringLength(255)]
		public string Nombresdeloshijos
		{
			get
			{
				var value = GetAttributeValue<string>("childrensnames");
				return value;
			}
			set
			{
				SetAttributeValue("childrensnames", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Company'.<br />
		/// Escriba el teléfono de la compañía del contacto.
		/// </summary>
		[AttributeLogicalName("company"), MaxLength(50), StringLength(50)]
		public string Telefonodelacompania
		{
			get
			{
				var value = GetAttributeValue<string>("company");
				return value;
			}
			set
			{
				SetAttributeValue("company", value);
			}
		}

		/// <summary>
		///  
		/// 'ContactId'.<br />
		/// Identificador único del contacto.
		/// </summary>
		[AttributeLogicalName("contactid")]
		public Guid? ContactoId
		{
			get
			{
				var value = GetAttributeValue<Guid?>("contactid");
				return value;
			}
			set
			{
				if (value != null)
					SetAttributeValue("contactid", value);
				if (value != null) base.Id = value.Value;
				else Id = System.Guid.Empty;
			}
		}

		/// <summary>
		///  
		/// 'CreatedBy'.<br />
		/// Muestra quién creó el registro.
		/// </summary>
		[AttributeLogicalName("createdby")]
		public Guid? Autor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Id;
			}
		}

		public EntityReference AutorReference => Autor == null ? null : GetAttributeValue<EntityReference>("createdby");

		public string AutorName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutorLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedByExternalParty'.<br />
		/// Muestra la parte externa que creó el registro.
		/// </summary>
		[AttributeLogicalName("createdbyexternalparty")]
		public Guid? Creadoporparteexterna
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdbyexternalparty");
				return value?.Id;
			}
		}

		public string CreadoporparteexternaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdbyexternalparty");
				return value?.Name;
			}
		}

		public IDictionary<int, string> CreadoporparteexternaLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedOn'.<br />
		/// Muestra la fecha y la hora en que se creó el registro. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("createdon")]
		public DateTime? Fechadecreacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("createdon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'CreatedOnBehalfBy'.<br />
		/// Muestra quién creó el registro en nombre de otro usuario.
		/// </summary>
		[AttributeLogicalName("createdonbehalfby")]
		public Guid? Autordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference AutordelegadoReference => Autordelegado == null ? null : GetAttributeValue<EntityReference>("createdonbehalfby");

		public string AutordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutordelegadoLabels { get; set; }

		/// <summary>
		/// [Range(0, 100000000000000)] 
		/// 'CreditLimit'.<br />
		/// Escriba el límite de crédito del contacto para tenerlo como referencia al tratar problemas de facturación y contabilidad con el cliente.
		/// </summary>
		[AttributeLogicalName("creditlimit"), Range(0, 100000000000000)]
		public decimal? Limitedelcredito
		{
			get
			{
				var value = GetAttributeValue<Money>("creditlimit");
				return value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("creditlimit", new Money(value.Value));
				else
					SetAttributeValue("creditlimit", value);
			}
		}

		/// <summary>
		/// [Range(-922337203685477, 922337203685477)] 
		/// 'CreditLimit_Base'.<br />
		/// Muestra el campo Límite de crédito convertido a la divisa base predeterminada del sistema para la generación de informes. Los cálculos utilizan el tipo de cambio especificado en el área Divisas.
		/// </summary>
		[AttributeLogicalName("creditlimit_base"), Range(-922337203685477, 922337203685477)]
		public decimal? Limitedecreditobase
		{
			get
			{
				var value = GetAttributeValue<Money>("creditlimit_base");
				return value?.Value;
			}
		}

		/// <summary>
		///  
		/// 'CreditOnHold'.<br />
		/// Seleccione si el contacto está en suspensión de crédito para tenerlo como referencia al tratar problemas de facturación y contabilidad.
		/// </summary>
		[AttributeLogicalName("creditonhold")]
		public bool? Suspensiondecredito
		{
			get
			{
				var value = GetAttributeValue<bool?>("creditonhold");
				return value;
			}
			set
			{
				SetAttributeValue("creditonhold", value);
			}
		}

		public IDictionary<int, string> SuspensiondecreditoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("creditonhold");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("creditonhold", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'CustomerSizeCode'.<br />
		/// Seleccione el tamaño de la compañía del contacto para segmentación y generación de informes.
		/// </summary>
		[AttributeLogicalName("customersizecode")]
		public TamanodelclienteEnum? Tamanodelcliente
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customersizecode");
				return (TamanodelclienteEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("customersizecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("customersizecode", value);
			}
		}

		public IDictionary<int, string> TamanodelclienteLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customersizecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("customersizecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'CustomerTypeCode'.<br />
		/// Seleccione la categoría que mejor describe la relación entre el contacto y la organización.
		/// </summary>
		[AttributeLogicalName("customertypecode")]
		public TipoderelacionEnum? Tipoderelacion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customertypecode");
				return (TipoderelacionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("customertypecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("customertypecode", value);
			}
		}

		public IDictionary<int, string> TipoderelacionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("customertypecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("customertypecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DefaultPriceLevelId'.<br />
		/// Elija la lista de precios predeterminada asociada al contacto para asegurarse de que se aplican los precios correctos de los productos para este cliente en oportunidades, ofertas y pedidos de venta.
		/// </summary>
		[AttributeLogicalName("defaultpricelevelid")]
		public Guid? Listadeprecios
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("defaultpricelevelid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("defaultpricelevelid", new EntityReference("pricelevel", value.Value));
				else
					SetAttributeValue("defaultpricelevelid", value);
			}
		}

		public string ListadepreciosName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("defaultpricelevelid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ListadepreciosLabels { get; set; }

		/// <summary>
		/// [MaxLength=100] 
		/// 'Department'.<br />
		/// Escriba el departamento o la unidad de negocio donde trabaja el contacto en la compañía o empresa primaria.
		/// </summary>
		[AttributeLogicalName("department"), MaxLength(100), StringLength(100)]
		public string Departamento
		{
			get
			{
				var value = GetAttributeValue<string>("department");
				return value;
			}
			set
			{
				SetAttributeValue("department", value);
			}
		}

		/// <summary>
		/// [MaxLength=2000] 
		/// 'Description'.<br />
		/// Escriba información adicional para describir el contacto, por ejemplo, un extracto del sitio web de la compañía.
		/// </summary>
		[AttributeLogicalName("description"), MaxLength(2000), StringLength(2000)]
		public string Descripcion
		{
			get
			{
				var value = GetAttributeValue<string>("description");
				return value;
			}
			set
			{
				SetAttributeValue("description", value);
			}
		}

		/// <summary>
		///  
		/// 'DoNotBulkEMail'.<br />
		/// Seleccione si el contacto acepta correo electrónico en masa a través de campañas de marketing o campañas exprés. Si selecciona No permitir, el contacto podrá agregarse a listas de marketing, pero quedará excluida del correo electrónico.
		/// </summary>
		[AttributeLogicalName("donotbulkemail")]
		public bool? Nopermitircorreoelecenmasa
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkemail");
				return value;
			}
			set
			{
				SetAttributeValue("donotbulkemail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoelecenmasaLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotbulkemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotBulkPostalMail'.<br />
		/// Seleccione si el contacto acepta correo postal masivo a través de campañas de marketing o campañas exprés. Si selecciona No permitir, el contacto podrá agregarse a listas de marketing, pero quedará excluida de las cartas.
		/// </summary>
		[AttributeLogicalName("donotbulkpostalmail")]
		public bool? Nopermitircorreoenmasa
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkpostalmail");
				return value;
			}
			set
			{
				SetAttributeValue("donotbulkpostalmail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoenmasaLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotbulkpostalmail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotbulkpostalmail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotEMail'.<br />
		/// Seleccione si el contacto permite enviar correo electrónico directo desde Microsoft Dynamics 365. Si se selecciona No permitir, Microsoft Dynamics 365 no enviará el correo electrónico.
		/// </summary>
		[AttributeLogicalName("donotemail")]
		public bool? Nopermitircorreoelectronico
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotemail");
				return value;
			}
			set
			{
				SetAttributeValue("donotemail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoelectronicoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotFax'.<br />
		/// Seleccione si el contacto permite faxes. Si selecciona No permitir, el contacto podrá quedar excluido de las actividades de fax distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotfax")]
		public bool? Nopermitirfaxes
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotfax");
				return value;
			}
			set
			{
				SetAttributeValue("donotfax", value);
			}
		}

		public IDictionary<int, string> NopermitirfaxesLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotfax");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotfax", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotPhone'.<br />
		/// Seleccione si el contacto acepta llamadas de teléfono. Si selecciona No permitir, el contacto podrá quedar excluido de las actividades de llamada de teléfono distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotphone")]
		public bool? Nopermitirllamadasdetelefono
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotphone");
				return value;
			}
			set
			{
				SetAttributeValue("donotphone", value);
			}
		}

		public IDictionary<int, string> NopermitirllamadasdetelefonoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotphone");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotphone", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotPostalMail'.<br />
		/// Seleccione si el contacto permite correo directo. Si selecciona No permitir, el contacto quedará excluido de las actividades de cartas distribuidas en campañas de marketing.
		/// </summary>
		[AttributeLogicalName("donotpostalmail")]
		public bool? Nopermitircorreo
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotpostalmail");
				return value;
			}
			set
			{
				SetAttributeValue("donotpostalmail", value);
			}
		}

		public IDictionary<int, string> NopermitircorreoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotpostalmail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotpostalmail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'DoNotSendMM'.<br />
		/// Seleccione si el contacto acepta materiales de marketing, como folletos o catálogos. Los contactos que optan por no participar pueden quedar excluidos de iniciativas de marketing.
		/// </summary>
		[AttributeLogicalName("donotsendmm")]
		public bool? Enviarmaterialesdemarketing
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotsendmm");
				return value;
			}
			set
			{
				SetAttributeValue("donotsendmm", value);
			}
		}

		public IDictionary<int, string> EnviarmaterialesdemarketingLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("donotsendmm");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("donotsendmm", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'EducationCode'.<br />
		/// Seleccione el nivel superior de educación del contacto para su uso en segmentación y análisis.
		/// </summary>
		[AttributeLogicalName("educationcode")]
		public EducacionEnum? Educacion
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("educationcode");
				return (EducacionEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("educationcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("educationcode", value);
			}
		}

		public IDictionary<int, string> EducacionLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("educationcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("educationcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress1'.<br />
		/// Escriba la dirección de correo electrónico principal para el contacto.
		/// </summary>
		[AttributeLogicalName("emailaddress1"), MaxLength(100), StringLength(100)]
		public string Correoelectronico
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress1");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress1", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress2'.<br />
		/// Escriba la dirección de correo electrónico secundaria para el contacto.
		/// </summary>
		[AttributeLogicalName("emailaddress2"), MaxLength(100), StringLength(100)]
		public string Direcciondecorreoelectronico2
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress2");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress2", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'EMailAddress3'.<br />
		/// Escriba una dirección de correo electrónico alternativa para el contacto.
		/// </summary>
		[AttributeLogicalName("emailaddress3"), MaxLength(100), StringLength(100)]
		public string Direcciondecorreoelectronico3
		{
			get
			{
				var value = GetAttributeValue<string>("emailaddress3");
				return value;
			}
			set
			{
				SetAttributeValue("emailaddress3", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'EmployeeId'.<br />
		/// Escriba el identificador o número de empleado del contacto para tenerlo como referencia en pedidos, casos de servicio u otras comunicaciones con la organización del contacto.
		/// </summary>
		[AttributeLogicalName("employeeid"), MaxLength(50), StringLength(50)]
		public string Empleado
		{
			get
			{
				var value = GetAttributeValue<string>("employeeid");
				return value;
			}
			set
			{
				SetAttributeValue("employeeid", value);
			}
		}

		/// <summary>
		///  
		/// 'EntityImage'.<br />
		/// Muestra la imagen predeterminada del registro.
		/// </summary>
		[AttributeLogicalName("entityimage"), MaxLength(10240000), MaxWidth(144), MaxHeight(144)]
		public byte[] Imagendelaentidad
		{
			get
			{
				var value = GetAttributeValue<byte[]>("entityimage");
				return value;
			}
			set
			{
				SetAttributeValue("entityimage", value);
			}
		}

		/// <summary>
		///  
		/// 'EntityImageId'.<br />
		/// Para uso interno.
		/// </summary>
		[AttributeLogicalName("entityimageid")]
		public Guid? Identificadordeimagendelaentidad
		{
			get
			{
				var value = GetAttributeValue<Guid?>("entityimageid");
				return value;
			}
		}

		/// <summary>
		/// [Range(0.0000000001, 100000000000)] 
		/// 'ExchangeRate'.<br />
		/// Muestra la tasa de conversión de la divisa del registro. El tipo de cambio se utiliza para convertir todos los campos de dinero del registro desde la divisa local hasta la divisa predeterminada del sistema.
		/// </summary>
		[AttributeLogicalName("exchangerate"), Range(0.0000000001, 100000000000)]
		public decimal? Tipodecambio
		{
			get
			{
				var value = GetAttributeValue<decimal?>("exchangerate");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'ExternalUserIdentifier'.<br />
		/// Identificador de un usuario externo.
		/// </summary>
		[AttributeLogicalName("externaluseridentifier"), MaxLength(50), StringLength(50)]
		public string Identificadordeusuarioexterno
		{
			get
			{
				var value = GetAttributeValue<string>("externaluseridentifier");
				return value;
			}
			set
			{
				SetAttributeValue("externaluseridentifier", value);
			}
		}

		/// <summary>
		///  
		/// 'FamilyStatusCode'.<br />
		/// Seleccione el estado civil del contacto para tenerlo como referencia en llamadas de teléfono de seguimiento u otras comunicaciones.
		/// </summary>
		[AttributeLogicalName("familystatuscode")]
		public EstadocivilEnum? Estadocivil
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("familystatuscode");
				return (EstadocivilEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("familystatuscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("familystatuscode", value);
			}
		}

		public IDictionary<int, string> EstadocivilLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("familystatuscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("familystatuscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Fax'.<br />
		/// Escriba el número de fax del contacto.
		/// </summary>
		[AttributeLogicalName("fax"), MaxLength(50), StringLength(50)]
		public string Fax
		{
			get
			{
				var value = GetAttributeValue<string>("fax");
				return value;
			}
			set
			{
				SetAttributeValue("fax", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'FirstName'.<br />
		/// Escriba el nombre de pila del contacto para asegurarse de que se dirige correctamente a él en llamadas de ventas, correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("firstname"), MaxLength(50), StringLength(50)]
		public string Nombredepila
		{
			get
			{
				var value = GetAttributeValue<string>("firstname");
				return value;
			}
			set
			{
				SetAttributeValue("firstname", value);
			}
		}

		/// <summary>
		///  
		/// 'FollowEmail'.<br />
		/// Información sobre si se permite el seguimiento de la actividad de correo, por ejemplo, abrir, ver datos adjuntos y hacer clic en vínculos de correos enviados al contacto.
		/// </summary>
		[AttributeLogicalName("followemail")]
		public bool? Seguiractividaddecorreo
		{
			get
			{
				var value = GetAttributeValue<bool?>("followemail");
				return value;
			}
			set
			{
				SetAttributeValue("followemail", value);
			}
		}

		public IDictionary<int, string> SeguiractividaddecorreoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("followemail");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("followemail", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'FtpSiteUrl'.<br />
		/// Escriba la dirección URL del sitio FTP del contacto para permitir a los usuarios acceder a datos y compartir documentos.
		/// </summary>
		[AttributeLogicalName("ftpsiteurl"), MaxLength(200), StringLength(200)]
		public string SitiodeFTP
		{
			get
			{
				var value = GetAttributeValue<string>("ftpsiteurl");
				return value;
			}
			set
			{
				SetAttributeValue("ftpsiteurl", value);
			}
		}

		/// <summary>
		/// [MaxLength=160] 
		/// 'FullName'.<br />
		/// Combina y muestra el nombre y los apellidos del contacto para que pueda mostrarse el nombre completo en vistas e informes.
		/// </summary>
		[AttributeLogicalName("fullname"), MaxLength(160), StringLength(160)]
		public string Nombrecompleto
		{
			get
			{
				var value = GetAttributeValue<string>("fullname");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'GenderCode'.<br />
		/// Seleccione el sexo del contacto para asegurarse de que se dirige correctamente a él en llamadas de ventas, correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("gendercode")]
		public SexoEnum? Sexo
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("gendercode");
				return (SexoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("gendercode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("gendercode", value);
			}
		}

		public IDictionary<int, string> SexoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("gendercode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("gendercode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'GovernmentId'.<br />
		/// Escriba el número de pasaporte u otra identificación gubernamental del contacto para su uso en documentos e informes.
		/// </summary>
		[AttributeLogicalName("governmentid"), MaxLength(50), StringLength(50)]
		public string Gubernamental
		{
			get
			{
				var value = GetAttributeValue<string>("governmentid");
				return value;
			}
			set
			{
				SetAttributeValue("governmentid", value);
			}
		}

		/// <summary>
		///  
		/// 'HasChildrenCode'.<br />
		/// Seleccione si el contacto tiene hijos para tenerlo como referencia en llamadas de teléfono de seguimiento u otras comunicaciones.
		/// </summary>
		[AttributeLogicalName("haschildrencode")]
		public TienehijosEnum? Tienehijos
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("haschildrencode");
				return (TienehijosEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("haschildrencode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("haschildrencode", value);
			}
		}

		public IDictionary<int, string> TienehijosLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("haschildrencode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("haschildrencode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Home2'.<br />
		/// Escriba un segundo número de teléfono particular para este contacto.
		/// </summary>
		[AttributeLogicalName("home2"), MaxLength(50), StringLength(50)]
		public string Telefonoparticular2
		{
			get
			{
				var value = GetAttributeValue<string>("home2");
				return value;
			}
			set
			{
				SetAttributeValue("home2", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'ImportSequenceNumber'.<br />
		/// Identificador único de la importación o la migración de datos que creó este registro.
		/// </summary>
		[AttributeLogicalName("importsequencenumber"), Range(-2147483648, 2147483647)]
		public int? Numerodesecuenciadeimportacion
		{
			get
			{
				var value = GetAttributeValue<int?>("importsequencenumber");
				return value;
			}
			set
			{
				SetAttributeValue("importsequencenumber", value);
			}
		}

		/// <summary>
		///  
		/// 'IsBackofficeCustomer'.<br />
		/// Seleccione si el contacto existe en una contabilidad aparte u otro sistema, como Microsoft Dynamics GP u otra base de datos de ERP, para su uso en procesos de integración.
		/// </summary>
		[AttributeLogicalName("isbackofficecustomer")]
		public bool? Clientedeserviciodegestion
		{
			get
			{
				var value = GetAttributeValue<bool?>("isbackofficecustomer");
				return value;
			}
			set
			{
				SetAttributeValue("isbackofficecustomer", value);
			}
		}

		public IDictionary<int, string> ClientedeserviciodegestionLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("isbackofficecustomer");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("isbackofficecustomer", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'JobTitle'.<br />
		/// Escriba el puesto del contacto para asegurarse de que se dirige correctamente a él en llamadas de ventas, correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("jobtitle"), MaxLength(100), StringLength(100)]
		public string Puesto
		{
			get
			{
				var value = GetAttributeValue<string>("jobtitle");
				return value;
			}
			set
			{
				SetAttributeValue("jobtitle", value);
			}
		}

		/// <summary>
		/// [Required][MaxLength=50] 
		/// 'LastName'.<br />
		/// Escriba el apellido del contacto para asegurarse de que se dirige correctamente a él en llamadas de ventas, correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("lastname"), Required, MaxLength(50), StringLength(50)]
		public string Apellidos
		{
			get
			{
				var value = GetAttributeValue<string>("lastname");
				return value;
			}
			set
			{
				SetAttributeValue("lastname", value);
			}
		}

		/// <summary>
		///  
		/// 'LastOnHoldTime'.<br />
		/// Contiene la marca de fecha y hora del último período de retención.
		/// </summary>
		[AttributeLogicalName("lastonholdtime")]
		public DateTime? Ultimoperiododeretencion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("lastonholdtime");
				return value;
			}
			set
			{
				SetAttributeValue("lastonholdtime", value);
			}
		}

		/// <summary>
		///  
		/// 'LastUsedInCampaign'.<br />
		/// Muestra la fecha en la que el contacto se incluyó por última vez en una campaña de marketing o una campaña exprés.
		/// </summary>
		[AttributeLogicalName("lastusedincampaign")]
		public DateTime? Ultimodiaincluidoenlacampana
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("lastusedincampaign");
				return value;
			}
			set
			{
				SetAttributeValue("lastusedincampaign", value);
			}
		}

		/// <summary>
		///  
		/// 'LeadSourceCode'.<br />
		/// Seleccione el origen de marketing principal que dirigió el contacto a su organización.
		/// </summary>
		[AttributeLogicalName("leadsourcecode")]
		public OrigendelclientepotencialEnum? Origendelclientepotencial
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("leadsourcecode");
				return (OrigendelclientepotencialEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("leadsourcecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("leadsourcecode", value);
			}
		}

		public IDictionary<int, string> OrigendelclientepotencialLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("leadsourcecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("leadsourcecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'ManagerName'.<br />
		/// Escriba el nombre del jefe del contacto para su uso al remitir a una instancia superior problemas u otras comunicaciones de seguimiento con el contacto.
		/// </summary>
		[AttributeLogicalName("managername"), MaxLength(100), StringLength(100)]
		public string Administrador
		{
			get
			{
				var value = GetAttributeValue<string>("managername");
				return value;
			}
			set
			{
				SetAttributeValue("managername", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'ManagerPhone'.<br />
		/// Escriba el número de teléfono del jefe del contacto.
		/// </summary>
		[AttributeLogicalName("managerphone"), MaxLength(50), StringLength(50)]
		public string Telefonodeljefe
		{
			get
			{
				var value = GetAttributeValue<string>("managerphone");
				return value;
			}
			set
			{
				SetAttributeValue("managerphone", value);
			}
		}

		/// <summary>
		///  
		/// 'MarketingOnly'.<br />
		/// Indica si es solo para marketing
		/// </summary>
		[AttributeLogicalName("marketingonly")]
		public bool? Soloparamarketing
		{
			get
			{
				var value = GetAttributeValue<bool?>("marketingonly");
				return value;
			}
			set
			{
				SetAttributeValue("marketingonly", value);
			}
		}

		public IDictionary<int, string> SoloparamarketingLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("marketingonly");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("marketingonly", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'MasterId'.<br />
		/// Identificador único del contacto principal para combinación.
		/// </summary>
		[AttributeLogicalName("masterid")]
		public Guid? Idmaestro
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("masterid");
				return value?.Id;
			}
		}

		public string IdmaestroName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("masterid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> IdmaestroLabels { get; set; }

		/// <summary>
		///  
		/// 'Merged'.<br />
		/// Muestra si se ha combinado la cuenta con un contacto principal.
		/// </summary>
		[AttributeLogicalName("merged")]
		public bool? Combinado
		{
			get
			{
				var value = GetAttributeValue<bool?>("merged");
				return value;
			}
		}

		public IDictionary<int, string> CombinadoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("merged");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("merged", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'MiddleName'.<br />
		/// Escriba el segundo nombre o la inicial del contacto para asegurarse de que se dirige correctamente a él.
		/// </summary>
		[AttributeLogicalName("middlename"), MaxLength(50), StringLength(50)]
		public string Segundonombre
		{
			get
			{
				var value = GetAttributeValue<string>("middlename");
				return value;
			}
			set
			{
				SetAttributeValue("middlename", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'MobilePhone'.<br />
		/// Escriba el número de teléfono móvil del contacto.
		/// </summary>
		[AttributeLogicalName("mobilephone"), MaxLength(50), StringLength(50)]
		public string Telefonomovil
		{
			get
			{
				var value = GetAttributeValue<string>("mobilephone");
				return value;
			}
			set
			{
				SetAttributeValue("mobilephone", value);
			}
		}

		/// <summary>
		///  
		/// 'ModifiedBy'.<br />
		/// Muestra quién actualizó el registro por última vez.
		/// </summary>
		[AttributeLogicalName("modifiedby")]
		public Guid? Modificadopor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Id;
			}
		}

		public EntityReference ModificadoporReference => Modificadopor == null ? null : GetAttributeValue<EntityReference>("modifiedby");

		public string ModificadoporName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedByExternalParty'.<br />
		/// Muestra la parte externa que modificó el registro.
		/// </summary>
		[AttributeLogicalName("modifiedbyexternalparty")]
		public Guid? Modificadoporparteexterna
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedbyexternalparty");
				return value?.Id;
			}
		}

		public string ModificadoporparteexternaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedbyexternalparty");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporparteexternaLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedOn'.<br />
		/// Muestra la fecha y la hora en que se actualizó el registro por última vez. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("modifiedon")]
		public DateTime? Fechademodificacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("modifiedon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'ModifiedOnBehalfBy'.<br />
		/// Muestra quién actualizó el registro en nombre de otro usuario por última vez.
		/// </summary>
		[AttributeLogicalName("modifiedonbehalfby")]
		public Guid? Modificadopordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference ModificadopordelegadoReference => Modificadopordelegado == null ? null : GetAttributeValue<EntityReference>("modifiedonbehalfby");

		public string ModificadopordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadopordelegadoLabels { get; set; }

		/// <summary>
		///  
		/// 'msdyn_gdproptout'.<br />
		/// Describe si se opta por recibir o no el contacto
		/// </summary>
		[AttributeLogicalName("msdyn_gdproptout")]
		public bool? OptarpornorecibirRGPD
		{
			get
			{
				var value = GetAttributeValue<bool?>("msdyn_gdproptout");
				return value;
			}
			set
			{
				SetAttributeValue("msdyn_gdproptout", value);
			}
		}

		public IDictionary<int, string> OptarpornorecibirRGPDLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("msdyn_gdproptout");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("msdyn_gdproptout", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'msdyn_orgchangestatus'.<br />
		/// Si el contacto pertenece o no a la cuenta asociada
		/// </summary>
		[AttributeLogicalName("msdyn_orgchangestatus")]
		public MarcaNoestaenlacompaniaEnum? MarcaNoestaenlacompania
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("msdyn_orgchangestatus");
				return (MarcaNoestaenlacompaniaEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("msdyn_orgchangestatus", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("msdyn_orgchangestatus", value);
			}
		}

		public IDictionary<int, string> MarcaNoestaenlacompaniaLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("msdyn_orgchangestatus");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("msdyn_orgchangestatus", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'NickName'.<br />
		/// Escriba el sobrenombre del contacto.
		/// </summary>
		[AttributeLogicalName("nickname"), MaxLength(100), StringLength(100)]
		public string Sobrenombre
		{
			get
			{
				var value = GetAttributeValue<string>("nickname");
				return value;
			}
			set
			{
				SetAttributeValue("nickname", value);
			}
		}

		/// <summary>
		/// [Range(0, 1000000000)] 
		/// 'NumberOfChildren'.<br />
		/// Escriba el número de hijos del contacto para tenerlo como referencia en llamadas de teléfono de seguimiento u otras comunicaciones.
		/// </summary>
		[AttributeLogicalName("numberofchildren"), Range(0, 1000000000)]
		public int? N_dehijos
		{
			get
			{
				var value = GetAttributeValue<int?>("numberofchildren");
				return value;
			}
			set
			{
				SetAttributeValue("numberofchildren", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OnHoldTime'.<br />
		/// Muestra durante cuánto tiempo, en minutos, se retuvo el registro.
		/// </summary>
		[AttributeLogicalName("onholdtime"), Range(-2147483648, 2147483647)]
		public int? Periododeretencionminutos
		{
			get
			{
				var value = GetAttributeValue<int?>("onholdtime");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'OriginatingLeadId'.<br />
		/// Muestra el cliente potencial a partir del cual se creó el contacto si este se creó convirtiendo un cliente potencial a Microsoft Dynamics 365. Se usa para relacionar el contacto con los datos del cliente potencial original para su uso en informes y análisis.
		/// </summary>
		[AttributeLogicalName("originatingleadid")]
		public Guid? Clientepotencialoriginal
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("originatingleadid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("originatingleadid", new EntityReference("lead", value.Value));
				else
					SetAttributeValue("originatingleadid", value);
			}
		}

		public EntityReference ClientepotencialoriginalReference => Clientepotencialoriginal == null ? null : GetAttributeValue<EntityReference>("originatingleadid");

		public string ClientepotencialoriginalName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("originatingleadid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ClientepotencialoriginalLabels { get; set; }

		/// <summary>
		///  
		/// 'OverriddenCreatedOn'.<br />
		/// Fecha y hora en que se migró el registro.
		/// </summary>
		[AttributeLogicalName("overriddencreatedon")]
		public DateTime? Fechadecreaciondelregistro
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("overriddencreatedon");
				return value;
			}
			set
			{
				SetAttributeValue("overriddencreatedon", value);
			}
		}

		/// <summary>
		///  
		/// 'OwnerId'.<br />
		/// Escriba el usuario o el equipo que está asignado para administrar el registro. Este campo se actualiza cada vez que se asigna el registro a otro usuario.
		/// </summary>
		[AttributeLogicalName("ownerid")]
		public EntityReference Propietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value;
			}
			set
			{
				SetAttributeValue("ownerid", value);
			}
		}

		public string PropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> PropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningBusinessUnit'.<br />
		/// Identificador único de la unidad de negocio propietaria del contacto.
		/// </summary>
		[AttributeLogicalName("owningbusinessunit")]
		public Guid? Unidaddenegociopropietaria
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Id;
			}
		}

		public string UnidaddenegociopropietariaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UnidaddenegociopropietariaLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningTeam'.<br />
		/// Identificador único del equipo propietario del contacto.
		/// </summary>
		[AttributeLogicalName("owningteam")]
		public Guid? Equipopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Id;
			}
		}

		public EntityReference EquipopropietarioReference => Equipopropietario == null ? null : GetAttributeValue<EntityReference>("owningteam");

		public string EquipopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Name;
			}
		}

		public IDictionary<int, string> EquipopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningUser'.<br />
		/// Identificador único del usuario propietario del contacto.
		/// </summary>
		[AttributeLogicalName("owninguser")]
		public Guid? Usuariopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Id;
			}
		}

		public EntityReference UsuariopropietarioReference => Usuariopropietario == null ? null : GetAttributeValue<EntityReference>("owninguser");

		public string UsuariopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopropietarioLabels { get; set; }

		/// <summary>
		/// [MaxLength=50] 
		/// 'Pager'.<br />
		/// Escriba el número de localizador del contacto.
		/// </summary>
		[AttributeLogicalName("pager"), MaxLength(50), StringLength(50)]
		public string Localizador
		{
			get
			{
				var value = GetAttributeValue<string>("pager");
				return value;
			}
			set
			{
				SetAttributeValue("pager", value);
			}
		}

		/// <summary>
		///  
		/// 'parent_contactid'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("parent_contactid")]
		public Guid? parent_contactid
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parent_contactid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("parent_contactid", new EntityReference("contact", value.Value));
				else
					SetAttributeValue("parent_contactid", value);
			}
		}

		public string parent_contactidName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parent_contactid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> parent_contactidLabels { get; set; }

		/// <summary>
		///  
		/// 'ParentContactId'.<br />
		/// Identificador único del contacto primario.
		/// </summary>
		[AttributeLogicalName("parentcontactid")]
		public Guid? Contactoprimario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentcontactid");
				return value?.Id;
			}
		}

		public string ContactoprimarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentcontactid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ContactoprimarioLabels { get; set; }

		/// <summary>
		///  
		/// 'ParentCustomerId'.<br />
		/// Seleccione la cuenta primaria o el contacto primario del contacto para proporcionar un vínculo rápido a detalles adicionales, como información financiera, actividades y oportunidades.
		/// </summary>
		[AttributeLogicalName("parentcustomerid")]
		public EntityReference Nombredelaescuela
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentcustomerid");
				return value;
			}
			set
			{
				SetAttributeValue("parentcustomerid", value);
			}
		}

		public string NombredelaescuelaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("parentcustomerid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> NombredelaescuelaLabels { get; set; }

		/// <summary>
		///  
		/// 'ParticipatesInWorkflow'.<br />
		/// Muestra si el contacto participa en reglas de flujo de trabajo.
		/// </summary>
		[AttributeLogicalName("participatesinworkflow")]
		public bool? Participaenflujodetrabajo
		{
			get
			{
				var value = GetAttributeValue<bool?>("participatesinworkflow");
				return value;
			}
			set
			{
				SetAttributeValue("participatesinworkflow", value);
			}
		}

		public IDictionary<int, string> ParticipaenflujodetrabajoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("participatesinworkflow");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("participatesinworkflow", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PaymentTermsCode'.<br />
		/// Seleccione las condiciones de pago para indicar cuándo debe pagar el cliente el importe total.
		/// </summary>
		[AttributeLogicalName("paymenttermscode")]
		public CondicionesdepagoEnum? Condicionesdepago
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("paymenttermscode");
				return (CondicionesdepagoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("paymenttermscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("paymenttermscode", value);
			}
		}

		public IDictionary<int, string> CondicionesdepagoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("paymenttermscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("paymenttermscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredAppointmentDayCode'.<br />
		/// Seleccione el día de la semana preferido para citas de servicio.
		/// </summary>
		[AttributeLogicalName("preferredappointmentdaycode")]
		public DiapreferidoEnum? Diapreferido
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmentdaycode");
				return (DiapreferidoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredappointmentdaycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredappointmentdaycode", value);
			}
		}

		public IDictionary<int, string> DiapreferidoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmentdaycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredappointmentdaycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredAppointmentTimeCode'.<br />
		/// Seleccione la hora preferida del día para citas de servicio.
		/// </summary>
		[AttributeLogicalName("preferredappointmenttimecode")]
		public HorapreferidaEnum? Horapreferida
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmenttimecode");
				return (HorapreferidaEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredappointmenttimecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredappointmenttimecode", value);
			}
		}

		public IDictionary<int, string> HorapreferidaLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredappointmenttimecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredappointmenttimecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredContactMethodCode'.<br />
		/// Seleccione el método de contacto preferido.
		/// </summary>
		[AttributeLogicalName("preferredcontactmethodcode")]
		public MetododecontactopreferidoEnum? Metododecontactopreferido
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredcontactmethodcode");
				return (MetododecontactopreferidoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredcontactmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("preferredcontactmethodcode", value);
			}
		}

		public IDictionary<int, string> MetododecontactopreferidoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("preferredcontactmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("preferredcontactmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'PreferredEquipmentId'.<br />
		/// Elija las instalaciones o el equipamiento de servicio preferido del contacto para asegurarse de que los servicios se programan correctamente para el cliente.
		/// </summary>
		[AttributeLogicalName("preferredequipmentid")]
		public Guid? Instalacequipampreferidos
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredequipmentid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredequipmentid", new EntityReference("equipment", value.Value));
				else
					SetAttributeValue("preferredequipmentid", value);
			}
		}

		public string InstalacequipampreferidosName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredequipmentid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> InstalacequipampreferidosLabels { get; set; }

		/// <summary>
		///  
		/// 'PreferredServiceId'.<br />
		/// Elija el servicio preferido del contacto para asegurarse de que los servicios se programan correctamente para el cliente.
		/// </summary>
		[AttributeLogicalName("preferredserviceid")]
		public Guid? Serviciopreferido
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredserviceid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredserviceid", new EntityReference("service", value.Value));
				else
					SetAttributeValue("preferredserviceid", value);
			}
		}

		public string ServiciopreferidoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredserviceid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ServiciopreferidoLabels { get; set; }

		/// <summary>
		///  
		/// 'PreferredSystemUserId'.<br />
		/// Elija el representante de servicio al cliente regular o preferido para tenerlo como referencia cuando programe actividades de servicio para el contacto.
		/// </summary>
		[AttributeLogicalName("preferredsystemuserid")]
		public Guid? Usuariopreferido
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredsystemuserid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("preferredsystemuserid", new EntityReference("systemuser", value.Value));
				else
					SetAttributeValue("preferredsystemuserid", value);
			}
		}

		public EntityReference UsuariopreferidoReference => Usuariopreferido == null ? null : GetAttributeValue<EntityReference>("preferredsystemuserid");

		public string UsuariopreferidoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("preferredsystemuserid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopreferidoLabels { get; set; }

		/// <summary>
		///  
		/// 'ProcessId'.<br />
		/// Muestra el identificador del proceso.
		/// </summary>
		[AttributeLogicalName("processid")]
		public Guid? Proceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("processid");
				return value;
			}
			set
			{
				SetAttributeValue("processid", value);
			}
		}

		/// <summary>
		/// [MaxLength=100] 
		/// 'Salutation'.<br />
		/// Escriba el saludo del contacto para asegurarse de que se dirige correctamente a él en llamadas de ventas, mensajes de correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("salutation"), MaxLength(100), StringLength(100)]
		public string Saludo
		{
			get
			{
				var value = GetAttributeValue<string>("salutation");
				return value;
			}
			set
			{
				SetAttributeValue("salutation", value);
			}
		}

		/// <summary>
		///  
		/// 'ShippingMethodCode'.<br />
		/// Seleccione un método de envío para las entregas enviadas a esta dirección.
		/// </summary>
		[AttributeLogicalName("shippingmethodcode")]
		public MododeenvioEnum? Mododeenvio
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("shippingmethodcode");
				return (MododeenvioEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("shippingmethodcode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("shippingmethodcode", value);
			}
		}

		public IDictionary<int, string> MododeenvioLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("shippingmethodcode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("shippingmethodcode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'SLAId'.<br />
		/// Elija el contrato de nivel de servicio (SLA) que desea aplicar al registro de contacto.
		/// </summary>
		[AttributeLogicalName("slaid")]
		public Guid? SLA
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("slaid", new EntityReference("sla", value.Value));
				else
					SetAttributeValue("slaid", value);
			}
		}

		public string SLAName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> SLALabels { get; set; }

		/// <summary>
		///  
		/// 'SLAInvokedId'.<br />
		/// Último SLA que se aplicó a este caso. Este campo es solo para uso interno.
		/// </summary>
		[AttributeLogicalName("slainvokedid")]
		public Guid? UltimoSLAaplicado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Id;
			}
		}

		public string UltimoSLAaplicadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UltimoSLAaplicadoLabels { get; set; }

		/// <summary>
		/// [MaxLength=100] 
		/// 'SpousesName'.<br />
		/// Escriba el nombre del cónyuge o la pareja del contacto para tenerlo como referencia en llamadas, eventos u otras comunicaciones con el contacto.
		/// </summary>
		[AttributeLogicalName("spousesname"), MaxLength(100), StringLength(100)]
		public string Nombredelconyugeopareja
		{
			get
			{
				var value = GetAttributeValue<string>("spousesname");
				return value;
			}
			set
			{
				SetAttributeValue("spousesname", value);
			}
		}

		/// <summary>
		///  
		/// 'StageId'.<br />
		/// Muestra el identificador de la fase.
		/// </summary>
		[AttributeLogicalName("stageid")]
		public Guid? __ObsoletoFasedeproceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("stageid");
				return value;
			}
			set
			{
				SetAttributeValue("stageid", value);
			}
		}

		/// <summary>
		///  
		/// 'StateCode'.<br />
		/// Muestra si el contacto está activo o inactivo. Los contactos inactivos son de solo lectura y no se pueden editar si no se reactivan.
		/// </summary>
		[AttributeLogicalName("statecode")]
		public EstadoEnum? Estado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				return (EstadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statecode", value);
			}
		}

		public IDictionary<int, string> EstadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'StatusCode'.<br />
		/// Seleccione el estado del contacto.
		/// </summary>
		[AttributeLogicalName("statuscode")]
		public RazonparaelestadoEnum? Razonparaelestado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				return (RazonparaelestadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statuscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statuscode", value);
			}
		}

		public IDictionary<int, string> RazonparaelestadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statuscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=10] 
		/// 'Suffix'.<br />
		/// Escriba el sufijo utilizado en el nombre del contacto, como Jr. o Sr., para asegurarse de que se dirige correctamente a él en llamadas de ventas, correo electrónico y campañas de marketing.
		/// </summary>
		[AttributeLogicalName("suffix"), MaxLength(10), StringLength(10)]
		public string Sufijo
		{
			get
			{
				var value = GetAttributeValue<string>("suffix");
				return value;
			}
			set
			{
				SetAttributeValue("suffix", value);
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'TeamsFollowed'.<br />
		/// Número de usuarios o conversaciones que siguieron el registro
		/// </summary>
		[AttributeLogicalName("teamsfollowed"), Range(-2147483648, 2147483647)]
		public int? Equiposseguidos
		{
			get
			{
				var value = GetAttributeValue<int?>("teamsfollowed");
				return value;
			}
			set
			{
				SetAttributeValue("teamsfollowed", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone1'.<br />
		/// Escriba el número de teléfono principal para este contacto.
		/// </summary>
		[AttributeLogicalName("telephone1"), MaxLength(50), StringLength(50)]
		public string Telefonodeltrabajo
		{
			get
			{
				var value = GetAttributeValue<string>("telephone1");
				return value;
			}
			set
			{
				SetAttributeValue("telephone1", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone2'.<br />
		/// Escriba un segundo número de teléfono para este contacto.
		/// </summary>
		[AttributeLogicalName("telephone2"), MaxLength(50), StringLength(50)]
		public string Telefonoparticular
		{
			get
			{
				var value = GetAttributeValue<string>("telephone2");
				return value;
			}
			set
			{
				SetAttributeValue("telephone2", value);
			}
		}

		/// <summary>
		/// [MaxLength=50] 
		/// 'Telephone3'.<br />
		/// Escriba un tercer número de teléfono para este contacto.
		/// </summary>
		[AttributeLogicalName("telephone3"), MaxLength(50), StringLength(50)]
		public string Telefono3
		{
			get
			{
				var value = GetAttributeValue<string>("telephone3");
				return value;
			}
			set
			{
				SetAttributeValue("telephone3", value);
			}
		}

		/// <summary>
		///  
		/// 'TerritoryCode'.<br />
		/// Seleccione una región o un territorio del contacto para su uso en segmentación y análisis.
		/// </summary>
		[AttributeLogicalName("territorycode")]
		public ZonadeventasEnum? Zonadeventas
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("territorycode");
				return (ZonadeventasEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("territorycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("territorycode", value);
			}
		}

		public IDictionary<int, string> ZonadeventasLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("territorycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("territorycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=1250] 
		/// 'TimeSpentByMeOnEmailAndMeetings'.<br />
		/// Tiempo total que dedico a correos (leer y escribir) y reuniones relacionados con el registro de contacto.
		/// </summary>
		[AttributeLogicalName("timespentbymeonemailandmeetings"), MaxLength(1250), StringLength(1250)]
		public string Tiempodedicadopormi
		{
			get
			{
				var value = GetAttributeValue<string>("timespentbymeonemailandmeetings");
				return value;
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'TimeZoneRuleVersionNumber'.<br />
		/// Para uso interno.
		/// </summary>
		[AttributeLogicalName("timezoneruleversionnumber"), Range(-1, 2147483647)]
		public int? Numerodeversionderegladezonahoraria
		{
			get
			{
				var value = GetAttributeValue<int?>("timezoneruleversionnumber");
				return value;
			}
			set
			{
				SetAttributeValue("timezoneruleversionnumber", value);
			}
		}

		/// <summary>
		///  
		/// 'TransactionCurrencyId'.<br />
		/// Elija la divisa local del registro para asegurarse de que en los presupuestos se utiliza la divisa correcta.
		/// </summary>
		[AttributeLogicalName("transactioncurrencyid")]
		public Guid? Divisa
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("transactioncurrencyid", new EntityReference("transactioncurrency", value.Value));
				else
					SetAttributeValue("transactioncurrencyid", value);
			}
		}

		public string DivisaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> DivisaLabels { get; set; }

		/// <summary>
		/// [MaxLength=1250] 
		/// 'TraversedPath'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("traversedpath"), MaxLength(1250), StringLength(1250)]
		public string __ObsoletoRutarecorrida
		{
			get
			{
				var value = GetAttributeValue<string>("traversedpath");
				return value;
			}
			set
			{
				SetAttributeValue("traversedpath", value);
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'UTCConversionTimeZoneCode'.<br />
		/// Código de la zona horaria que estaba en uso cuando se creó el registro.
		/// </summary>
		[AttributeLogicalName("utcconversiontimezonecode"), Range(-1, 2147483647)]
		public int? CodigodezonahorariadeconversionUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("utcconversiontimezonecode");
				return value;
			}
			set
			{
				SetAttributeValue("utcconversiontimezonecode", value);
			}
		}

		/// <summary>
		///  
		/// 'VersionNumber'.<br />
		/// Número de versión del contacto.
		/// </summary>
		[AttributeLogicalName("versionnumber")]
		public long? Numerodeversion
		{
			get
			{
				var value = GetAttributeValue<long?>("versionnumber");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=200] 
		/// 'WebSiteUrl'.<br />
		/// Escriba la dirección URL del sitio web o del blog profesional o personal del contacto.
		/// </summary>
		[AttributeLogicalName("websiteurl"), MaxLength(200), StringLength(200)]
		public string Sitioweb
		{
			get
			{
				var value = GetAttributeValue<string>("websiteurl");
				return value;
			}
			set
			{
				SetAttributeValue("websiteurl", value);
			}
		}

		/// <summary>
		/// [MaxLength=150] 
		/// 'YomiFirstName'.<br />
		/// Escriba la transcripción fonética del nombre de pila del contacto, si se especifica en japonés, para asegurarse de pronunciarlo correctamente en llamadas de teléfono con el contacto.
		/// </summary>
		[AttributeLogicalName("yomifirstname"), MaxLength(150), StringLength(150)]
		public string NombredepilaYomi
		{
			get
			{
				var value = GetAttributeValue<string>("yomifirstname");
				return value;
			}
			set
			{
				SetAttributeValue("yomifirstname", value);
			}
		}

		/// <summary>
		/// [MaxLength=450] 
		/// 'YomiFullName'.<br />
		/// Muestra el nombre y los apellidos Yomi del contacto para que pueda mostrarse el nombre fonético completo en vistas e informes.
		/// </summary>
		[AttributeLogicalName("yomifullname"), MaxLength(450), StringLength(450)]
		public string NombreYomicompleto
		{
			get
			{
				var value = GetAttributeValue<string>("yomifullname");
				return value;
			}
		}

		/// <summary>
		/// [MaxLength=150] 
		/// 'YomiLastName'.<br />
		/// Escriba la transcripción fonética del apellido del contacto, si se especifica en japonés, para asegurarse de pronunciarlo correctamente en llamadas de teléfono con el contacto.
		/// </summary>
		[AttributeLogicalName("yomilastname"), MaxLength(150), StringLength(150)]
		public string ApellidosYomi
		{
			get
			{
				var value = GetAttributeValue<string>("yomilastname");
				return value;
			}
			set
			{
				SetAttributeValue("yomilastname", value);
			}
		}

		/// <summary>
		/// [MaxLength=150] 
		/// 'YomiMiddleName'.<br />
		/// Escriba la transcripción fonética del segundo nombre del contacto, si se especifica en japonés, para asegurarse de pronunciarlo correctamente en llamadas de teléfono con el contacto.
		/// </summary>
		[AttributeLogicalName("yomimiddlename"), MaxLength(150), StringLength(150)]
		public string SegundonombreYomi
		{
			get
			{
				var value = GetAttributeValue<string>("yomimiddlename");
				return value;
			}
			set
			{
				SetAttributeValue("yomimiddlename", value);
			}
		}

		#endregion

		#region Relationships


		public static class RelationNames
		{
		}

		public override IDictionary<string, object[]> RelationProperties
		{
			get
			{
				if (relationProperties != null) return relationProperties;
				relationProperties = new Dictionary<string, object[]>();
				return relationProperties;
			}
		}

		#endregion

		/// <inheritdoc/>
		public Profesor(object obj) : base(obj, EntityLogicalName)
		{
			foreach (var p in obj.GetType().GetProperties())
			{
				var value = p.GetValue(obj, null);
				if (p.PropertyType == typeof(Guid))
				{
					base.Id = (Guid)value;
					Attributes["contactid"] = base.Id;
				}
				else if (p.Name == "FormattedValues")
				{
					FormattedValues.AddRange((FormattedValueCollection)value);
				}
				else
				{
					Attributes[p.Name.ToLower()] = value;
				}
			}
		}

		#region Label/value pairs

		public enum RolEnum
		{
			Responsabledetomadedecisiones = 1,
			Empleado = 2,
			Personaconinfluencia = 3,
		}

		public enum Direccion1tipodedireccionEnum
		{
			Facturacion = 1,
			Envio = 2,
			Primario = 3,
			Otros = 4,
		}

		public enum Direccion1condicionesdefleteEnum
		{
			CFRCosteyflete = 3,
			CIFCosteseguroyflete = 4,
			CIPFleteysegurospagadoshasta = 5,
			CPTFletepagadohasta = 6,
			DAFFrancoenfrontera = 7,
			DEQFrancosobremuelle = 8,
			DESFrancoexship = 9,
			DDPFrancodespachadoenaduanas = 10,
			DDUFrancosindespacharenaduanas = 11,
			EXWEnfabrica = 12,
			FASFrancocostadobuque = 13,
			FCAFrancotransportista = 14,
			Otros = 15,
			EXQSobremuelle = 16,
			EXSExship = 17,
			FOAFrancoaeropuerto = 18,
			FORFrancovagon = 19,
			FRCFrancotransportista = 20,
			DCPelvendedorcorrecontodoslosgastoshastallegaraldestinoacordadolosriesgossetransfierenalcompradorcuandoseentregalamercanciaalprimertransportistaenelpuntodedestino = 23,
			FONFrancoabordo = 27,
		}

		public enum Direccion1mododeenvioEnum
		{
			Aereo = 1,
			DHL = 2,
			UPS = 4,
			CorreoPostal = 5,
			Cargacompleta = 6,
			Recogidaacargodelcliente = 7,
			Carretera = 9,
			Ferrocarril = 10,
			Maritimo = 11,
			Urgente = 39,
			SEUR = 40,
			MRW = 41,
			NACEX = 42,
			Grupaje = 43,
			Servicioadomicilio = 44,
		}

		public enum Direccion2tipodedireccionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion2condicionesdefleteEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion2mododeenvioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion3tipodedireccionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion3condicionesdefleteEnum
		{
			Valorpredeterminado = 1,
		}

		public enum Direccion3mododeenvioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum SuspensiondecreditoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum TamanodelclienteEnum
		{
			Valorpredeterminado = 1,
		}

		public enum TipoderelacionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum NopermitircorreoelecenmasaEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitircorreoenmasaEnum
		{
			Si = 1,
			No = 0,
		}

		public enum NopermitircorreoelectronicoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitirfaxesEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitirllamadasdetelefonoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum NopermitircorreoEnum
		{
			Nopermitir = 1,
			Permitir = 0,
		}

		public enum EnviarmaterialesdemarketingEnum
		{
			Noenviar = 1,
			Enviar = 0,
		}

		public enum EducacionEnum
		{
			Valorpredeterminado = 1,
		}

		public enum EstadocivilEnum
		{
			Solteroa = 1,
			Casadoa = 2,
			Divorciadoa = 3,
			Viudoa = 4,
		}

		public enum SeguiractividaddecorreoEnum
		{
			Permitir = 1,
			Nopermitir = 0,
		}

		public enum SexoEnum
		{
			Hombre = 1,
			Mujer = 2,
		}

		public enum TienehijosEnum
		{
			Valorpredeterminado = 1,
		}

		public enum ClientedeserviciodegestionEnum
		{
			Si = 1,
			No = 0,
		}

		public enum OrigendelclientepotencialEnum
		{
			Valorpredeterminado = 1,
		}

		public enum SoloparamarketingEnum
		{
			Si = 1,
			No = 0,
		}

		public enum CombinadoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum OptarpornorecibirRGPDEnum
		{
			Si = 1,
			No = 0,
		}

		public enum MarcaNoestaenlacompaniaEnum
		{
			Nohaycomentarios = 0,
			Noestaenlacompania = 1,
			Omitir = 2,
		}

		public enum ParticipaenflujodetrabajoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum CondicionesdepagoEnum
		{
			Pagoa30dias = 1,
			_210pagoa30dias = 2,
			Pagoa45dias = 3,
			Pagoa60dias = 4,
			Pagoalcontado = 10,
			_306090 = 38,
			Aplazado = 39,
		}

		public enum DiapreferidoEnum
		{
			Domingo = 0,
			Lunes = 1,
			Martes = 2,
			Miercoles = 3,
			Jueves = 4,
			Viernes = 5,
			Sabado = 6,
		}

		public enum HorapreferidaEnum
		{
			Manana = 1,
			Tarde = 2,
			Ultimahora = 3,
		}

		public enum MetododecontactopreferidoEnum
		{
			Cualquiera = 1,
			Correoelectronico = 2,
			Telefono = 3,
			Fax = 4,
			Correo = 5,
		}

		public enum MododeenvioEnum
		{
			Valorpredeterminado = 1,
		}

		public enum EstadoEnum
		{
			Activo = 0,
			Inactivo = 1,
		}

		public enum RazonparaelestadoEnum
		{
			Activo = 1,
			Inactivo = 2,
		}

		public enum ZonadeventasEnum
		{
			Valorpredeterminado = 1,
		}

		#endregion

		#region Metadata

		#region Enums

		public static class Enums
		{
			/// <summary>
			/// Gets the label corresponding to the option-set's value using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="constant">The value from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns></returns>
			public static string GetLabel(string logicalName, int constant, int languageCode = 1033)
			{
				return GeneratorHelpers.GetLabel(logicalName, constant, typeof(Enums), languageCode);
			}
			/// <summary>
			/// Gets the value corresponding to the option-set's label using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="label">The label from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns>The value corresponding to the label</returns>
			public static int GetValue(string logicalName, string label, int languageCode = 1033)
			{
				return GeneratorHelpers.GetValue(logicalName, label, typeof(Enums), languageCode);
			}
		}

		#endregion

		#region Fields

		public static class Fields
		{
			#region Logical names

			public const string Cuenta = "accountid";
			public const string Rol = "accountrolecode";
			public const string Direccion1tipodedireccion = "address1_addresstypecode";
			public const string Direccion1ciudad = "address1_city";
			public const string Direccion1 = "address1_composite";
			public const string Direccion1paisoregion = "address1_country";
			public const string Direccion1condado = "address1_county";
			public const string Direccion1fax = "address1_fax";
			public const string Direccion1condicionesdeflete = "address1_freighttermscode";
			public const string Direccion1latitud = "address1_latitude";
			public const string Direccion1calle1 = "address1_line1";
			public const string Direccion1calle2 = "address1_line2";
			public const string Direccion1calle3 = "address1_line3";
			public const string Direccion1longitud = "address1_longitude";
			public const string Direccion1nombre = "address1_name";
			public const string Direccion1codigopostal = "address1_postalcode";
			public const string Direccion1apartadodecorreos = "address1_postofficebox";
			public const string Direccion1nomcontactoppal = "address1_primarycontactname";
			public const string Direccion1mododeenvio = "address1_shippingmethodcode";
			public const string Direccion1estadooprovincia = "address1_stateorprovince";
			public const string Direccion1telefono = "address1_telephone1";
			public const string Direccion1telefono2 = "address1_telephone2";
			public const string Direccion1telefono3 = "address1_telephone3";
			public const string Direccion1zonadeUPS = "address1_upszone";
			public const string Direccion1desplazdeUTC = "address1_utcoffset";
			public const string Direccion2tipodedireccion = "address2_addresstypecode";
			public const string Direccion2ciudad = "address2_city";
			public const string Direccion2 = "address2_composite";
			public const string Direccion2paisoregion = "address2_country";
			public const string Direccion2condado = "address2_county";
			public const string Direccion2fax = "address2_fax";
			public const string Direccion2condicionesdeflete = "address2_freighttermscode";
			public const string Direccion2latitud = "address2_latitude";
			public const string Direccion2calle1 = "address2_line1";
			public const string Direccion2calle2 = "address2_line2";
			public const string Direccion2calle3 = "address2_line3";
			public const string Direccion2longitud = "address2_longitude";
			public const string Direccion2nombre = "address2_name";
			public const string Direccion2codigopostal = "address2_postalcode";
			public const string Direccion2apartadodecorreos = "address2_postofficebox";
			public const string Direccion2nomcontactoppal = "address2_primarycontactname";
			public const string Direccion2mododeenvio = "address2_shippingmethodcode";
			public const string Direccion2estadooprovincia = "address2_stateorprovince";
			public const string Direccion2telefono1 = "address2_telephone1";
			public const string Direccion2telefono2 = "address2_telephone2";
			public const string Direccion2telefono3 = "address2_telephone3";
			public const string Direccion2zonadeUPS = "address2_upszone";
			public const string Direccion2desplazdeUTC = "address2_utcoffset";
			public const string Direccion3tipodedireccion = "address3_addresstypecode";
			public const string Direccion3ciudad = "address3_city";
			public const string Direccion3 = "address3_composite";
			public const string Direccion3paisoregion = "address3_country";
			public const string Direccion3condado = "address3_county";
			public const string Direccion3fax = "address3_fax";
			public const string Direccion3condicionesdeflete = "address3_freighttermscode";
			public const string Direccion3latitud = "address3_latitude";
			public const string Direccion3calle1 = "address3_line1";
			public const string Direccion3calle2 = "address3_line2";
			public const string Direccion3calle3 = "address3_line3";
			public const string Direccion3longitud = "address3_longitude";
			public const string Direccion3nombre = "address3_name";
			public const string Direccion3codigopostal = "address3_postalcode";
			public const string Direccion3apartadodecorreos = "address3_postofficebox";
			public const string Direccion3nombredelcontactoprincipal = "address3_primarycontactname";
			public const string Direccion3mododeenvio = "address3_shippingmethodcode";
			public const string Direccion3estadooprovincia = "address3_stateorprovince";
			public const string Direccion3telefono1 = "address3_telephone1";
			public const string Direccion3telefono2 = "address3_telephone2";
			public const string Direccion3telefono3 = "address3_telephone3";
			public const string Direccion3zonadeUPS = "address3_upszone";
			public const string Direccion3desplazamientodeUTC = "address3_utcoffset";
			public const string Vence30 = "aging30";
			public const string Vence30base = "aging30_base";
			public const string Vence60 = "aging60";
			public const string Vence60base = "aging60_base";
			public const string Vence90 = "aging90";
			public const string Vence90base = "aging90_base";
			public const string Aniversario = "anniversary";
			public const string Ingresosanuales = "annualincome";
			public const string Ingresosanualesbase = "annualincome_base";
			public const string Ayudante = "assistantname";
			public const string Telefonodelayudante = "assistantphone";
			public const string Cumpleanos = "birthdate";
			public const string Telefonodeltrabajo2 = "business2";
			public const string Tarjetadepresentacion = "businesscard";
			public const string BusinessCardAttributes = "businesscardattributes";
			public const string Numerodedevoluciondellamada = "callback";
			public const string Nombresdeloshijos = "childrensnames";
			public const string Telefonodelacompania = "company";
			public const string ContactoId = "contactid";
			public const string Autor = "createdby";
			public const string Creadoporparteexterna = "createdbyexternalparty";
			public const string Fechadecreacion = "createdon";
			public const string Autordelegado = "createdonbehalfby";
			public const string Limitedelcredito = "creditlimit";
			public const string Limitedecreditobase = "creditlimit_base";
			public const string Suspensiondecredito = "creditonhold";
			public const string Tamanodelcliente = "customersizecode";
			public const string Tipoderelacion = "customertypecode";
			public const string Listadeprecios = "defaultpricelevelid";
			public const string Departamento = "department";
			public const string Descripcion = "description";
			public const string Nopermitircorreoelecenmasa = "donotbulkemail";
			public const string Nopermitircorreoenmasa = "donotbulkpostalmail";
			public const string Nopermitircorreoelectronico = "donotemail";
			public const string Nopermitirfaxes = "donotfax";
			public const string Nopermitirllamadasdetelefono = "donotphone";
			public const string Nopermitircorreo = "donotpostalmail";
			public const string Enviarmaterialesdemarketing = "donotsendmm";
			public const string Educacion = "educationcode";
			public const string Correoelectronico = "emailaddress1";
			public const string Direcciondecorreoelectronico2 = "emailaddress2";
			public const string Direcciondecorreoelectronico3 = "emailaddress3";
			public const string Empleado = "employeeid";
			public const string Imagendelaentidad = "entityimage";
			public const string Identificadordeimagendelaentidad = "entityimageid";
			public const string Tipodecambio = "exchangerate";
			public const string Identificadordeusuarioexterno = "externaluseridentifier";
			public const string Estadocivil = "familystatuscode";
			public const string Fax = "fax";
			public const string Nombredepila = "firstname";
			public const string Seguiractividaddecorreo = "followemail";
			public const string SitiodeFTP = "ftpsiteurl";
			public const string Nombrecompleto = "fullname";
			public const string Sexo = "gendercode";
			public const string Gubernamental = "governmentid";
			public const string Tienehijos = "haschildrencode";
			public const string Telefonoparticular2 = "home2";
			public const string Numerodesecuenciadeimportacion = "importsequencenumber";
			public const string Clientedeserviciodegestion = "isbackofficecustomer";
			public const string Puesto = "jobtitle";
			public const string Apellidos = "lastname";
			public const string Ultimoperiododeretencion = "lastonholdtime";
			public const string Ultimodiaincluidoenlacampana = "lastusedincampaign";
			public const string Origendelclientepotencial = "leadsourcecode";
			public const string Administrador = "managername";
			public const string Telefonodeljefe = "managerphone";
			public const string Soloparamarketing = "marketingonly";
			public const string Idmaestro = "masterid";
			public const string Combinado = "merged";
			public const string Segundonombre = "middlename";
			public const string Telefonomovil = "mobilephone";
			public const string Modificadopor = "modifiedby";
			public const string Modificadoporparteexterna = "modifiedbyexternalparty";
			public const string Fechademodificacion = "modifiedon";
			public const string Modificadopordelegado = "modifiedonbehalfby";
			public const string OptarpornorecibirRGPD = "msdyn_gdproptout";
			public const string MarcaNoestaenlacompania = "msdyn_orgchangestatus";
			public const string Sobrenombre = "nickname";
			public const string N_dehijos = "numberofchildren";
			public const string Periododeretencionminutos = "onholdtime";
			public const string Clientepotencialoriginal = "originatingleadid";
			public const string Fechadecreaciondelregistro = "overriddencreatedon";
			public const string Propietario = "ownerid";
			public const string Unidaddenegociopropietaria = "owningbusinessunit";
			public const string Equipopropietario = "owningteam";
			public const string Usuariopropietario = "owninguser";
			public const string Localizador = "pager";
			public const string parent_contactid = "parent_contactid";
			public const string Contactoprimario = "parentcontactid";
			public const string Nombredelaescuela = "parentcustomerid";
			public const string Participaenflujodetrabajo = "participatesinworkflow";
			public const string Condicionesdepago = "paymenttermscode";
			public const string Diapreferido = "preferredappointmentdaycode";
			public const string Horapreferida = "preferredappointmenttimecode";
			public const string Metododecontactopreferido = "preferredcontactmethodcode";
			public const string Instalacequipampreferidos = "preferredequipmentid";
			public const string Serviciopreferido = "preferredserviceid";
			public const string Usuariopreferido = "preferredsystemuserid";
			public const string Proceso = "processid";
			public const string Saludo = "salutation";
			public const string Mododeenvio = "shippingmethodcode";
			public const string SLA = "slaid";
			public const string UltimoSLAaplicado = "slainvokedid";
			public const string Nombredelconyugeopareja = "spousesname";
			public const string __ObsoletoFasedeproceso = "stageid";
			public const string Estado = "statecode";
			public const string Razonparaelestado = "statuscode";
			public const string Sufijo = "suffix";
			public const string Equiposseguidos = "teamsfollowed";
			public const string Telefonodeltrabajo = "telephone1";
			public const string Telefonoparticular = "telephone2";
			public const string Telefono3 = "telephone3";
			public const string Zonadeventas = "territorycode";
			public const string Tiempodedicadopormi = "timespentbymeonemailandmeetings";
			public const string Numerodeversionderegladezonahoraria = "timezoneruleversionnumber";
			public const string Divisa = "transactioncurrencyid";
			public const string __ObsoletoRutarecorrida = "traversedpath";
			public const string CodigodezonahorariadeconversionUTC = "utcconversiontimezonecode";
			public const string Numerodeversion = "versionnumber";
			public const string Sitioweb = "websiteurl";
			public const string NombredepilaYomi = "yomifirstname";
			public const string NombreYomicompleto = "yomifullname";
			public const string ApellidosYomi = "yomilastname";
			public const string SegundonombreYomi = "yomimiddlename";

			#endregion
		}

		#endregion

		#endregion
	}

	#endregion

	#region Ejecuciondeactualizacion

	/// <summary>
	/// 'msdyn_upgraderun'.<br />
	/// Contiene información de registro sobre una ejecución de un paquete de Package Deployer que actualiza una solución
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract, EntityLogicalName("msdyn_upgraderun")]
	public partial class Ejecuciondeactualizacion : GeneratedEntity<Ejecuciondeactualizacion.RelationName>
	{
		public Ejecuciondeactualizacion() : base(EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Ejecuciondeactualizacion(string[] keys, object[] values) : base(keys, values, EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Ejecuciondeactualizacion(object obj, Type limitingType) : base(obj, limitingType, EntityLogicalName)
		{ }

		public const string DisplayName = "Ejecución de actualización";
		public const string SchemaName = "msdyn_upgraderun";
		public const string EntityLogicalName = "msdyn_upgraderun";
		public const int EntityTypeCode = 10102;

		public class RelationName : RelationNameBase
		{
			public RelationName(string name) : base(name)
			{ }
		}
		#region Metadata

		#region Enums

		public static class Enums
		{
			/// <summary>
			/// Gets the label corresponding to the option-set's value using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="constant">The value from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns></returns>
			public static string GetLabel(string logicalName, int constant, int languageCode = 1033)
			{
				return GeneratorHelpers.GetLabel(logicalName, constant, typeof(Enums), languageCode);
			}
			/// <summary>
			/// Gets the value corresponding to the option-set's label using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="label">The label from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns>The value corresponding to the label</returns>
			public static int GetValue(string logicalName, string label, int languageCode = 1033)
			{
				return GeneratorHelpers.GetValue(logicalName, label, typeof(Enums), languageCode);
			}
		}

		#endregion

		#endregion
	}

	#endregion

	#region Expediente

	/// <summary>
	/// 'new_file'.<br />
	/// 
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract, EntityLogicalName("new_file")]
	public partial class Expediente : GeneratedEntity<Expediente.RelationName>
	{
		public Expediente() : base(EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Expediente(string[] keys, object[] values) : base(keys, values, EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Expediente(object obj, Type limitingType) : base(obj, limitingType, EntityLogicalName)
		{ }

		public const string DisplayName = "Expediente";
		public const string SchemaName = "new_file";
		public const string EntityLogicalName = "new_file";
		public const int EntityTypeCode = 10333;

		public class RelationName : RelationNameBase
		{
			public RelationName(string name) : base(name)
			{ }
		}

		#region Attributes

		[AttributeLogicalName("new_fileid")]
		public override System.Guid Id
		{
			get => (ExpedienteId == null || ExpedienteId == Guid.Empty) ? base.Id : ExpedienteId.GetValueOrDefault();
			set
			{
				if (value == Guid.Empty)
				{
					Attributes.Remove("new_fileid");
					base.Id = value;
				}
				else
				{
					ExpedienteId = value;
				}
			}
		}

		/// <summary>
		///  
		/// 'CreatedBy'.<br />
		/// Identificador único del usuario que creó el registro.
		/// </summary>
		[AttributeLogicalName("createdby")]
		public Guid? Autor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Id;
			}
		}

		public EntityReference AutorReference => Autor == null ? null : GetAttributeValue<EntityReference>("createdby");

		public string AutorName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutorLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedOn'.<br />
		/// Fecha y hora de creación del registro.
		/// </summary>
		[AttributeLogicalName("createdon")]
		public DateTime? Fechadecreacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("createdon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'CreatedOnBehalfBy'.<br />
		/// Identificador único del usuario delegado que creó el registro.
		/// </summary>
		[AttributeLogicalName("createdonbehalfby")]
		public Guid? Autordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference AutordelegadoReference => Autordelegado == null ? null : GetAttributeValue<EntityReference>("createdonbehalfby");

		public string AutordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutordelegadoLabels { get; set; }

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'ImportSequenceNumber'.<br />
		/// Número de secuencia de la importación que creó este registro.
		/// </summary>
		[AttributeLogicalName("importsequencenumber"), Range(-2147483648, 2147483647)]
		public int? Numerodesecuenciadeimportacion
		{
			get
			{
				var value = GetAttributeValue<int?>("importsequencenumber");
				return value;
			}
			set
			{
				SetAttributeValue("importsequencenumber", value);
			}
		}

		/// <summary>
		///  
		/// 'ModifiedBy'.<br />
		/// Identificador único del usuario que modificó el registro.
		/// </summary>
		[AttributeLogicalName("modifiedby")]
		public Guid? Modificadopor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Id;
			}
		}

		public EntityReference ModificadoporReference => Modificadopor == null ? null : GetAttributeValue<EntityReference>("modifiedby");

		public string ModificadoporName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedOn'.<br />
		/// Fecha y hora de modificación del registro.
		/// </summary>
		[AttributeLogicalName("modifiedon")]
		public DateTime? Fechademodificacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("modifiedon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'ModifiedOnBehalfBy'.<br />
		/// Identificador único del usuario delegado que modificó el registro.
		/// </summary>
		[AttributeLogicalName("modifiedonbehalfby")]
		public Guid? Modificadopordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference ModificadopordelegadoReference => Modificadopordelegado == null ? null : GetAttributeValue<EntityReference>("modifiedonbehalfby");

		public string ModificadopordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadopordelegadoLabels { get; set; }

		[AttributeLogicalName("new_description"), MaxLength(100), StringLength(100)]
		public string Descripcion
		{
			get
			{
				var value = GetAttributeValue<string>("new_description");
				return value;
			}
			set
			{
				SetAttributeValue("new_description", value);
			}
		}

		/// <summary>
		///  
		/// 'new_fileId'.<br />
		/// Identificador único de instancias de entidad
		/// </summary>
		[AttributeLogicalName("new_fileid")]
		public Guid? ExpedienteId
		{
			get
			{
				var value = GetAttributeValue<Guid?>("new_fileid");
				return value;
			}
			set
			{
				if (value != null)
					SetAttributeValue("new_fileid", value);
				if (value != null) base.Id = value.Value;
				else Id = System.Guid.Empty;
			}
		}

		[AttributeLogicalName("new_grade"), Range(-100000000000, 100000000000)]
		public decimal? NotaExpediente
		{
			get
			{
				var value = GetAttributeValue<decimal?>("new_grade");
				return value;
			}
			set
			{
				SetAttributeValue("new_grade", value);
			}
		}

		/// <summary>
		/// [Required][MaxLength=100] 
		/// 'new_numfile'.<br />
		/// Nombre de la entidad personalizada.
		/// </summary>
		[AttributeLogicalName("new_numfile"), Required, MaxLength(100), StringLength(100)]
		public string NumExpediente
		{
			get
			{
				var value = GetAttributeValue<string>("new_numfile");
				return value;
			}
			set
			{
				SetAttributeValue("new_numfile", value);
			}
		}

		/// <summary>
		///  
		/// 'OverriddenCreatedOn'.<br />
		/// Fecha y hora de migración del registro.
		/// </summary>
		[AttributeLogicalName("overriddencreatedon")]
		public DateTime? Fechadecreaciondelregistro
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("overriddencreatedon");
				return value;
			}
			set
			{
				SetAttributeValue("overriddencreatedon", value);
			}
		}

		/// <summary>
		///  
		/// 'OwnerId'.<br />
		/// Id. del propietario
		/// </summary>
		[AttributeLogicalName("ownerid")]
		public EntityReference Propietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value;
			}
			set
			{
				SetAttributeValue("ownerid", value);
			}
		}

		public string PropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> PropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningBusinessUnit'.<br />
		/// Identificador único de la unidad de negocio propietaria del registro.
		/// </summary>
		[AttributeLogicalName("owningbusinessunit")]
		public Guid? Unidaddenegociopropietaria
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Id;
			}
		}

		public string UnidaddenegociopropietariaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UnidaddenegociopropietariaLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningTeam'.<br />
		/// Identificador único del equipo propietario del registro.
		/// </summary>
		[AttributeLogicalName("owningteam")]
		public Guid? Equipopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Id;
			}
		}

		public EntityReference EquipopropietarioReference => Equipopropietario == null ? null : GetAttributeValue<EntityReference>("owningteam");

		public string EquipopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Name;
			}
		}

		public IDictionary<int, string> EquipopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningUser'.<br />
		/// Identificador único del usuario propietario del registro.
		/// </summary>
		[AttributeLogicalName("owninguser")]
		public Guid? Usuariopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Id;
			}
		}

		public EntityReference UsuariopropietarioReference => Usuariopropietario == null ? null : GetAttributeValue<EntityReference>("owninguser");

		public string UsuariopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'statecode'.<br />
		/// Estado del Expediente
		/// </summary>
		[AttributeLogicalName("statecode")]
		public EstadoEnum? Estado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				return (EstadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statecode", value);
			}
		}

		public IDictionary<int, string> EstadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'statuscode'.<br />
		/// Razón para el estado del Expediente
		/// </summary>
		[AttributeLogicalName("statuscode")]
		public RazonparaelestadoEnum? Razonparaelestado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				return (RazonparaelestadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statuscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statuscode", value);
			}
		}

		public IDictionary<int, string> RazonparaelestadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statuscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'TimeZoneRuleVersionNumber'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("timezoneruleversionnumber"), Range(-1, 2147483647)]
		public int? Numerodeversionderegladezonahoraria
		{
			get
			{
				var value = GetAttributeValue<int?>("timezoneruleversionnumber");
				return value;
			}
			set
			{
				SetAttributeValue("timezoneruleversionnumber", value);
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'UTCConversionTimeZoneCode'.<br />
		/// Código de la zona horaria en uso cuando se creó el registro.
		/// </summary>
		[AttributeLogicalName("utcconversiontimezonecode"), Range(-1, 2147483647)]
		public int? CodigodezonahorariadeconversionUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("utcconversiontimezonecode");
				return value;
			}
			set
			{
				SetAttributeValue("utcconversiontimezonecode", value);
			}
		}

		/// <summary>
		///  
		/// 'VersionNumber'.<br />
		/// Número de versión
		/// </summary>
		[AttributeLogicalName("versionnumber")]
		public long? Numerodeversion
		{
			get
			{
				var value = GetAttributeValue<long?>("versionnumber");
				return value;
			}
		}

		#endregion

		#region Relationships


		public static class RelationNames
		{
		}

		public override IDictionary<string, object[]> RelationProperties
		{
			get
			{
				if (relationProperties != null) return relationProperties;
				relationProperties = new Dictionary<string, object[]>();
				return relationProperties;
			}
		}

		#endregion

		/// <inheritdoc/>
		public Expediente(object obj) : base(obj, EntityLogicalName)
		{
			foreach (var p in obj.GetType().GetProperties())
			{
				var value = p.GetValue(obj, null);
				if (p.PropertyType == typeof(Guid))
				{
					base.Id = (Guid)value;
					Attributes["new_fileid"] = base.Id;
				}
				else if (p.Name == "FormattedValues")
				{
					FormattedValues.AddRange((FormattedValueCollection)value);
				}
				else
				{
					Attributes[p.Name.ToLower()] = value;
				}
			}
		}

		#region Label/value pairs

		public enum EstadoEnum
		{
			Activo = 0,
			Inactivo = 1,
		}

		public enum RazonparaelestadoEnum
		{
			Activo = 1,
			Inactivo = 2,
		}

		#endregion

		#region Metadata

		#region Enums

		public static class Enums
		{
			/// <summary>
			/// Gets the label corresponding to the option-set's value using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="constant">The value from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns></returns>
			public static string GetLabel(string logicalName, int constant, int languageCode = 1033)
			{
				return GeneratorHelpers.GetLabel(logicalName, constant, typeof(Enums), languageCode);
			}
			/// <summary>
			/// Gets the value corresponding to the option-set's label using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="label">The label from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns>The value corresponding to the label</returns>
			public static int GetValue(string logicalName, string label, int languageCode = 1033)
			{
				return GeneratorHelpers.GetValue(logicalName, label, typeof(Enums), languageCode);
			}
		}

		#endregion

		#region Fields

		public static class Fields
		{
			#region Logical names

			public const string Autor = "createdby";
			public const string Fechadecreacion = "createdon";
			public const string Autordelegado = "createdonbehalfby";
			public const string Numerodesecuenciadeimportacion = "importsequencenumber";
			public const string Modificadopor = "modifiedby";
			public const string Fechademodificacion = "modifiedon";
			public const string Modificadopordelegado = "modifiedonbehalfby";
			public const string Descripcion = "new_description";
			public const string ExpedienteId = "new_fileid";
			public const string NotaExpediente = "new_grade";
			public const string NumExpediente = "new_numfile";
			public const string Fechadecreaciondelregistro = "overriddencreatedon";
			public const string Propietario = "ownerid";
			public const string Unidaddenegociopropietaria = "owningbusinessunit";
			public const string Equipopropietario = "owningteam";
			public const string Usuariopropietario = "owninguser";
			public const string Estado = "statecode";
			public const string Razonparaelestado = "statuscode";
			public const string Numerodeversionderegladezonahoraria = "timezoneruleversionnumber";
			public const string CodigodezonahorariadeconversionUTC = "utcconversiontimezonecode";
			public const string Numerodeversion = "versionnumber";

			#endregion
		}

		#endregion

		#endregion
	}

	#endregion

	#region Tarea

	/// <summary>
	/// 'Task'.<br />
	/// Actividad genérica que representa el trabajo que se debe realizar.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract, EntityLogicalName("task")]
	public partial class Tarea : GeneratedEntity<Tarea.RelationName>
	{
		public Tarea() : base(EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Tarea(string[] keys, object[] values) : base(keys, values, EntityLogicalName)
		{ }

		/// <inheritdoc/>
		public Tarea(object obj, Type limitingType) : base(obj, limitingType, EntityLogicalName)
		{ }

		public const string DisplayName = "Tarea";
		public const string SchemaName = "Task";
		public const string EntityLogicalName = "task";
		public const int EntityTypeCode = 4212;

		public class RelationName : RelationNameBase
		{
			public RelationName(string name) : base(name)
			{ }
		}

		#region Attributes

		[AttributeLogicalName("activityid")]
		public override System.Guid Id
		{
			get => (TareaId == null || TareaId == Guid.Empty) ? base.Id : TareaId.GetValueOrDefault();
			set
			{
				if (value == Guid.Empty)
				{
					Attributes.Remove("activityid");
					base.Id = value;
				}
				else
				{
					TareaId = value;
				}
			}
		}

		/// <summary>
		/// [MaxLength=8192] 
		/// 'ActivityAdditionalParams'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("activityadditionalparams"), MaxLength(8192), StringLength(8192)]
		public string Parametrosadicionales
		{
			get
			{
				var value = GetAttributeValue<string>("activityadditionalparams");
				return value;
			}
			set
			{
				SetAttributeValue("activityadditionalparams", value);
			}
		}

		/// <summary>
		///  
		/// 'ActivityId'.<br />
		/// Identificador único de la tarea.
		/// </summary>
		[AttributeLogicalName("activityid")]
		public Guid? TareaId
		{
			get
			{
				var value = GetAttributeValue<Guid?>("activityid");
				return value;
			}
			set
			{
				if (value != null)
					SetAttributeValue("activityid", value);
				if (value != null) base.Id = value.Value;
				else Id = System.Guid.Empty;
			}
		}

		/// <summary>
		///  
		/// 'ActivityTypeCode'.<br />
		/// Tipo de actividad.
		/// </summary>
		[AttributeLogicalName("activitytypecode")]
		public string Tipodeactividad
		{
			get
			{
				var value = GetAttributeValue<string>("activitytypecode");
				return value;
			}
		}

		/// <summary>
		/// [Range(0, 2147483647)] 
		/// 'ActualDurationMinutes'.<br />
		/// Escriba el número de minutos invertidos en la tarea. La duración se utiliza en la generación de informes.
		/// </summary>
		[AttributeLogicalName("actualdurationminutes"), Range(0, 2147483647)]
		public int? Duracion
		{
			get
			{
				var value = GetAttributeValue<int?>("actualdurationminutes");
				return value;
			}
			set
			{
				SetAttributeValue("actualdurationminutes", value);
			}
		}

		/// <summary>
		///  
		/// 'ActualEnd'.<br />
		/// Especifique la fecha y la hora de finalización reales de la tarea. De forma predeterminada, muestra cuándo se completó o canceló la actividad.
		/// </summary>
		[AttributeLogicalName("actualend")]
		public DateTime? Finreal
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("actualend");
				return value;
			}
			set
			{
				SetAttributeValue("actualend", value);
			}
		}

		/// <summary>
		///  
		/// 'ActualStart'.<br />
		/// Escriba la fecha y la hora de inicio reales de la tarea. De forma predeterminada, muestra cuándo se creó la tarea.
		/// </summary>
		[AttributeLogicalName("actualstart")]
		public DateTime? Comienzoreal
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("actualstart");
				return value;
			}
			set
			{
				SetAttributeValue("actualstart", value);
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Category'.<br />
		/// Escriba una categoría para identificar el tipo de tarea, como recopilación de clientes potenciales o seguimiento de clientes, para enlazar la tarea a un grupo o una función de negocio.
		/// </summary>
		[AttributeLogicalName("category"), MaxLength(250), StringLength(250)]
		public string Categoria
		{
			get
			{
				var value = GetAttributeValue<string>("category");
				return value;
			}
			set
			{
				SetAttributeValue("category", value);
			}
		}

		/// <summary>
		///  
		/// 'CreatedBy'.<br />
		/// Muestra quién creó el registro.
		/// </summary>
		[AttributeLogicalName("createdby")]
		public Guid? Autor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Id;
			}
		}

		public EntityReference AutorReference => Autor == null ? null : GetAttributeValue<EntityReference>("createdby");

		public string AutorName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutorLabels { get; set; }

		/// <summary>
		///  
		/// 'CreatedOn'.<br />
		/// Muestra la fecha y la hora en que se creó el registro. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("createdon")]
		public DateTime? Fechadecreacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("createdon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'CreatedOnBehalfBy'.<br />
		/// Muestra quién creó el registro en nombre de otro usuario.
		/// </summary>
		[AttributeLogicalName("createdonbehalfby")]
		public Guid? Autordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference AutordelegadoReference => Autordelegado == null ? null : GetAttributeValue<EntityReference>("createdonbehalfby");

		public string AutordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("createdonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> AutordelegadoLabels { get; set; }

		/// <summary>
		///  
		/// 'CrmTaskAssignedUniqueId'.<br />
		/// Id. único de tarea asignada
		/// </summary>
		[AttributeLogicalName("crmtaskassigneduniqueid")]
		public Guid? Idunicodetareaasignada
		{
			get
			{
				var value = GetAttributeValue<Guid?>("crmtaskassigneduniqueid");
				return value;
			}
			set
			{
				SetAttributeValue("crmtaskassigneduniqueid", value);
			}
		}

		/// <summary>
		/// [MaxLength=2000] 
		/// 'Description'.<br />
		/// Escriba información adicional para describir la tarea.
		/// </summary>
		[AttributeLogicalName("description"), MaxLength(2000), StringLength(2000)]
		public string Descripcion
		{
			get
			{
				var value = GetAttributeValue<string>("description");
				return value;
			}
			set
			{
				SetAttributeValue("description", value);
			}
		}

		/// <summary>
		/// [Range(0.0000000001, 100000000000)] 
		/// 'ExchangeRate'.<br />
		/// Muestra la tasa de conversión de la divisa del registro. El tipo de cambio se utiliza para convertir todos los campos de dinero del registro desde la divisa local hasta la divisa predeterminada del sistema.
		/// </summary>
		[AttributeLogicalName("exchangerate"), Range(0.0000000001, 100000000000)]
		public decimal? Tipodecambio
		{
			get
			{
				var value = GetAttributeValue<decimal?>("exchangerate");
				return value;
			}
		}

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'ImportSequenceNumber'.<br />
		/// Identificador único de la importación o la migración de datos que creó este registro.
		/// </summary>
		[AttributeLogicalName("importsequencenumber"), Range(-2147483648, 2147483647)]
		public int? Numerodesecuenciadeimportacion
		{
			get
			{
				var value = GetAttributeValue<int?>("importsequencenumber");
				return value;
			}
			set
			{
				SetAttributeValue("importsequencenumber", value);
			}
		}

		/// <summary>
		///  
		/// 'IsBilled'.<br />
		/// Especifica si la tarea se facturó como parte de la resolución de un caso.
		/// </summary>
		[AttributeLogicalName("isbilled")]
		public bool? Estafacturado
		{
			get
			{
				var value = GetAttributeValue<bool?>("isbilled");
				return value;
			}
			set
			{
				SetAttributeValue("isbilled", value);
			}
		}

		public IDictionary<int, string> EstafacturadoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("isbilled");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("isbilled", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'IsRegularActivity'.<br />
		/// Información que especifica si la actividad es un tipo de evento o de actividad regular.
		/// </summary>
		[AttributeLogicalName("isregularactivity")]
		public bool? Esunaactividadregular
		{
			get
			{
				var value = GetAttributeValue<bool?>("isregularactivity");
				return value;
			}
		}

		public IDictionary<int, string> EsunaactividadregularLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("isregularactivity");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("isregularactivity", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'IsWorkflowCreated'.<br />
		/// Indica si la tarea se creó a partir de una regla de flujo de trabajo.
		/// </summary>
		[AttributeLogicalName("isworkflowcreated")]
		public bool? Estacreadoporflujodetrabajo
		{
			get
			{
				var value = GetAttributeValue<bool?>("isworkflowcreated");
				return value;
			}
			set
			{
				SetAttributeValue("isworkflowcreated", value);
			}
		}

		public IDictionary<int, string> EstacreadoporflujodetrabajoLabels
		{
			get
			{
				var value = GetAttributeValue<bool?>("isworkflowcreated");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("isworkflowcreated", (bool) value ? 1 : 0, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'LastOnHoldTime'.<br />
		/// Contiene la marca de fecha y hora del último período de retención.
		/// </summary>
		[AttributeLogicalName("lastonholdtime")]
		public DateTime? Ultimoperiododeretencion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("lastonholdtime");
				return value;
			}
			set
			{
				SetAttributeValue("lastonholdtime", value);
			}
		}

		/// <summary>
		///  
		/// 'ModifiedBy'.<br />
		/// Muestra quién actualizó el registro por última vez.
		/// </summary>
		[AttributeLogicalName("modifiedby")]
		public Guid? Modificadopor
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Id;
			}
		}

		public EntityReference ModificadoporReference => Modificadopor == null ? null : GetAttributeValue<EntityReference>("modifiedby");

		public string ModificadoporName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadoporLabels { get; set; }

		/// <summary>
		///  
		/// 'ModifiedOn'.<br />
		/// Muestra la fecha y la hora en que se actualizó el registro por última vez. La fecha y la hora se muestran en la zona horaria seleccionada en las opciones de Microsoft Dynamics 365.
		/// </summary>
		[AttributeLogicalName("modifiedon")]
		public DateTime? Fechademodificacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("modifiedon");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'ModifiedOnBehalfBy'.<br />
		/// Muestra quién actualizó el registro en nombre de otro usuario por última vez.
		/// </summary>
		[AttributeLogicalName("modifiedonbehalfby")]
		public Guid? Modificadopordelegado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Id;
			}
		}

		public EntityReference ModificadopordelegadoReference => Modificadopordelegado == null ? null : GetAttributeValue<EntityReference>("modifiedonbehalfby");

		public string ModificadopordelegadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("modifiedonbehalfby");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ModificadopordelegadoLabels { get; set; }

		/// <summary>
		/// [Range(-2147483648, 2147483647)] 
		/// 'OnHoldTime'.<br />
		/// Muestra durante cuánto tiempo, en minutos, se retuvo el registro.
		/// </summary>
		[AttributeLogicalName("onholdtime"), Range(-2147483648, 2147483647)]
		public int? Periododeretencionminutos
		{
			get
			{
				var value = GetAttributeValue<int?>("onholdtime");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'OverriddenCreatedOn'.<br />
		/// Fecha y hora en que se migró el registro.
		/// </summary>
		[AttributeLogicalName("overriddencreatedon")]
		public DateTime? Fechadecreaciondelregistro
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("overriddencreatedon");
				return value;
			}
			set
			{
				SetAttributeValue("overriddencreatedon", value);
			}
		}

		/// <summary>
		///  
		/// 'OwnerId'.<br />
		/// Escriba el usuario o el equipo que está asignado para administrar el registro. Este campo se actualiza cada vez que se asigna el registro a otro usuario.
		/// </summary>
		[AttributeLogicalName("ownerid")]
		public EntityReference Propietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value;
			}
			set
			{
				SetAttributeValue("ownerid", value);
			}
		}

		public string PropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("ownerid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> PropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningBusinessUnit'.<br />
		/// Muestra la unidad de negocio del propietario del registro.
		/// </summary>
		[AttributeLogicalName("owningbusinessunit")]
		public Guid? Unidaddenegociopropietaria
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Id;
			}
		}

		public string UnidaddenegociopropietariaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningbusinessunit");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UnidaddenegociopropietariaLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningTeam'.<br />
		/// Identificador único del equipo propietario de la tarea.
		/// </summary>
		[AttributeLogicalName("owningteam")]
		public Guid? Equipopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Id;
			}
		}

		public EntityReference EquipopropietarioReference => Equipopropietario == null ? null : GetAttributeValue<EntityReference>("owningteam");

		public string EquipopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owningteam");
				return value?.Name;
			}
		}

		public IDictionary<int, string> EquipopropietarioLabels { get; set; }

		/// <summary>
		///  
		/// 'OwningUser'.<br />
		/// Identificador único del usuario propietario de la tarea.
		/// </summary>
		[AttributeLogicalName("owninguser")]
		public Guid? Usuariopropietario
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Id;
			}
		}

		public EntityReference UsuariopropietarioReference => Usuariopropietario == null ? null : GetAttributeValue<EntityReference>("owninguser");

		public string UsuariopropietarioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("owninguser");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UsuariopropietarioLabels { get; set; }

		/// <summary>
		/// [Range(0, 100)] 
		/// 'PercentComplete'.<br />
		/// Escriba el valor del porcentaje total de la tarea para realizar un seguimiento de las tareas hasta su finalización.
		/// </summary>
		[AttributeLogicalName("percentcomplete"), Range(0, 100)]
		public int? Porcentajecompletado
		{
			get
			{
				var value = GetAttributeValue<int?>("percentcomplete");
				return value;
			}
			set
			{
				SetAttributeValue("percentcomplete", value);
			}
		}

		/// <summary>
		///  
		/// 'PriorityCode'.<br />
		/// Seleccione la prioridad de modo que se administren con rapidez las cuestiones críticas o preferidas por el cliente.
		/// </summary>
		[AttributeLogicalName("prioritycode")]
		public PrioridadEnum? Prioridad
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("prioritycode");
				return (PrioridadEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("prioritycode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("prioritycode", value);
			}
		}

		public IDictionary<int, string> PrioridadLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("prioritycode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("prioritycode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'ProcessId'.<br />
		/// Muestra el identificador del proceso.
		/// </summary>
		[AttributeLogicalName("processid")]
		public Guid? Proceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("processid");
				return value;
			}
			set
			{
				SetAttributeValue("processid", value);
			}
		}

		/// <summary>
		///  
		/// 'RegardingObjectId'.<br />
		/// Elija el registro relacionado con la tarea.
		/// </summary>
		[AttributeLogicalName("regardingobjectid")]
		public EntityReference Referentea
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("regardingobjectid");
				return value;
			}
			set
			{
				SetAttributeValue("regardingobjectid", value);
			}
		}

		public string ReferenteaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("regardingobjectid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ReferenteaLabels { get; set; }

		/// <summary>
		/// [Range(0, 2147483647)] 
		/// 'ScheduledDurationMinutes'.<br />
		/// Duración programada de la tarea en minutos.
		/// </summary>
		[AttributeLogicalName("scheduleddurationminutes"), Range(0, 2147483647)]
		public int? Duracionprogramada
		{
			get
			{
				var value = GetAttributeValue<int?>("scheduleddurationminutes");
				return value;
			}
		}

		/// <summary>
		///  
		/// 'ScheduledEnd'.<br />
		/// Escriba la fecha y la hora de vencimiento previstas.
		/// </summary>
		[AttributeLogicalName("scheduledend")]
		public DateTime? Fechadevencimiento
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("scheduledend");
				return value;
			}
			set
			{
				SetAttributeValue("scheduledend", value);
			}
		}

		/// <summary>
		///  
		/// 'ScheduledStart'.<br />
		/// Escriba la fecha y la hora de vencimiento previstas.
		/// </summary>
		[AttributeLogicalName("scheduledstart")]
		public DateTime? Fechadeinicio
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("scheduledstart");
				return value;
			}
			set
			{
				SetAttributeValue("scheduledstart", value);
			}
		}

		/// <summary>
		///  
		/// 'ServiceId'.<br />
		/// Elija el servicio que está asociado a esta actividad.
		/// </summary>
		[AttributeLogicalName("serviceid")]
		public Guid? Servicio
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("serviceid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("serviceid", new EntityReference("service", value.Value));
				else
					SetAttributeValue("serviceid", value);
			}
		}

		public string ServicioName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("serviceid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> ServicioLabels { get; set; }

		/// <summary>
		///  
		/// 'SLAId'.<br />
		/// Elija el contrato de nivel de servicio (SLA) que desea aplicar al registro de tarea.
		/// </summary>
		[AttributeLogicalName("slaid")]
		public Guid? SLA
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("slaid", new EntityReference("sla", value.Value));
				else
					SetAttributeValue("slaid", value);
			}
		}

		public string SLAName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slaid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> SLALabels { get; set; }

		/// <summary>
		///  
		/// 'SLAInvokedId'.<br />
		/// Último SLA que se aplicó a esta tarea. Este campo es solo para uso interno.
		/// </summary>
		[AttributeLogicalName("slainvokedid")]
		public Guid? UltimoSLAaplicado
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Id;
			}
		}

		public string UltimoSLAaplicadoName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("slainvokedid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> UltimoSLAaplicadoLabels { get; set; }

		/// <summary>
		///  
		/// 'SortDate'.<br />
		/// Muestra la fecha y la hora por las que se ordenan las actividades.
		/// </summary>
		[AttributeLogicalName("sortdate")]
		public DateTime? Fechadeordenacion
		{
			get
			{
				var value = GetAttributeValue<DateTime?>("sortdate");
				return value;
			}
			set
			{
				SetAttributeValue("sortdate", value);
			}
		}

		/// <summary>
		///  
		/// 'StageId'.<br />
		/// Muestra el identificador de la fase.
		/// </summary>
		[AttributeLogicalName("stageid")]
		public Guid? __ObsoletoFasedeproceso
		{
			get
			{
				var value = GetAttributeValue<Guid?>("stageid");
				return value;
			}
			set
			{
				SetAttributeValue("stageid", value);
			}
		}

		/// <summary>
		///  
		/// 'StateCode'.<br />
		/// Muestra si la tarea está abierta, completada o cancelada. Las tareas completadas y canceladas son de solo lectura y no se pueden editar.
		/// </summary>
		[AttributeLogicalName("statecode")]
		public EstadodelaactividadEnum? Estadodelaactividad
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				return (EstadodelaactividadEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statecode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statecode", value);
			}
		}

		public IDictionary<int, string> EstadodelaactividadLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statecode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statecode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		///  
		/// 'StatusCode'.<br />
		/// Seleccione el estado de la tarea.
		/// </summary>
		[AttributeLogicalName("statuscode")]
		public RazonparaelestadoEnum? Razonparaelestado
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				return (RazonparaelestadoEnum?)value?.Value;
			}
			set
			{
				if (value != null) SetAttributeValue("statuscode", new OptionSetValue((int)value.Value));
				else
					SetAttributeValue("statuscode", value);
			}
		}

		public IDictionary<int, string> RazonparaelestadoLabels
		{
			get
			{
				var value = GetAttributeValue<OptionSetValue>("statuscode");
				if (value == null) return null;
				return new Dictionary<int, string>
						{
							 { 3082, Enums.GetLabel("statuscode", value.Value, 3082) },
						};
			}
		}

		/// <summary>
		/// [MaxLength=250] 
		/// 'Subcategory'.<br />
		/// Escriba una subcategoría para identificar el tipo de tarea y relacione la actividad con un producto, una región de ventas, un grupo de negocio u otra función específica.
		/// </summary>
		[AttributeLogicalName("subcategory"), MaxLength(250), StringLength(250)]
		public string Subcategoria
		{
			get
			{
				var value = GetAttributeValue<string>("subcategory");
				return value;
			}
			set
			{
				SetAttributeValue("subcategory", value);
			}
		}

		/// <summary>
		/// [Required][MaxLength=200] 
		/// 'Subject'.<br />
		/// Escriba una breve descripción del objetivo o tema principal de la tarea.
		/// </summary>
		[AttributeLogicalName("subject"), Required, MaxLength(200), StringLength(200)]
		public string Asunto
		{
			get
			{
				var value = GetAttributeValue<string>("subject");
				return value;
			}
			set
			{
				SetAttributeValue("subject", value);
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'TimeZoneRuleVersionNumber'.<br />
		/// Para uso interno.
		/// </summary>
		[AttributeLogicalName("timezoneruleversionnumber"), Range(-1, 2147483647)]
		public int? Numerodeversionderegladezonahoraria
		{
			get
			{
				var value = GetAttributeValue<int?>("timezoneruleversionnumber");
				return value;
			}
			set
			{
				SetAttributeValue("timezoneruleversionnumber", value);
			}
		}

		/// <summary>
		///  
		/// 'TransactionCurrencyId'.<br />
		/// Elija la divisa local del registro para asegurarse de que en los presupuestos se utiliza la divisa correcta.
		/// </summary>
		[AttributeLogicalName("transactioncurrencyid")]
		public Guid? Divisa
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Id;
			}
			set
			{
				if (value != null) SetAttributeValue("transactioncurrencyid", new EntityReference("transactioncurrency", value.Value));
				else
					SetAttributeValue("transactioncurrencyid", value);
			}
		}

		public string DivisaName
		{
			get
			{
				var value = GetAttributeValue<EntityReference>("transactioncurrencyid");
				return value?.Name;
			}
		}

		public IDictionary<int, string> DivisaLabels { get; set; }

		/// <summary>
		/// [MaxLength=1250] 
		/// 'TraversedPath'.<br />
		/// Solo para uso interno.
		/// </summary>
		[AttributeLogicalName("traversedpath"), MaxLength(1250), StringLength(1250)]
		public string __ObsoletoRutarecorrida
		{
			get
			{
				var value = GetAttributeValue<string>("traversedpath");
				return value;
			}
			set
			{
				SetAttributeValue("traversedpath", value);
			}
		}

		/// <summary>
		/// [Range(-1, 2147483647)] 
		/// 'UTCConversionTimeZoneCode'.<br />
		/// Código de la zona horaria que estaba en uso cuando se creó el registro.
		/// </summary>
		[AttributeLogicalName("utcconversiontimezonecode"), Range(-1, 2147483647)]
		public int? CodigodezonahorariadeconversionUTC
		{
			get
			{
				var value = GetAttributeValue<int?>("utcconversiontimezonecode");
				return value;
			}
			set
			{
				SetAttributeValue("utcconversiontimezonecode", value);
			}
		}

		/// <summary>
		///  
		/// 'VersionNumber'.<br />
		/// Número de versión de la tarea.
		/// </summary>
		[AttributeLogicalName("versionnumber")]
		public long? Numerodeversion
		{
			get
			{
				var value = GetAttributeValue<long?>("versionnumber");
				return value;
			}
		}

		#endregion

		#region Relationships


		public static class RelationNames
		{
		}

		public override IDictionary<string, object[]> RelationProperties
		{
			get
			{
				if (relationProperties != null) return relationProperties;
				relationProperties = new Dictionary<string, object[]>();
				return relationProperties;
			}
		}

		#endregion

		/// <inheritdoc/>
		public Tarea(object obj) : base(obj, EntityLogicalName)
		{
			foreach (var p in obj.GetType().GetProperties())
			{
				var value = p.GetValue(obj, null);
				if (p.PropertyType == typeof(Guid))
				{
					base.Id = (Guid)value;
					Attributes["activityid"] = base.Id;
				}
				else if (p.Name == "FormattedValues")
				{
					FormattedValues.AddRange((FormattedValueCollection)value);
				}
				else
				{
					Attributes[p.Name.ToLower()] = value;
				}
			}
		}

		#region Label/value pairs

		public enum TipodeactividadEnum
		{
			Fax = 4204,
			Llamadadetelefono = 4210,
			Correoelectronico = 4202,
			Carta = 4207,
			Cita = 4201,
			Tarea = 4212,
			Citaperiodica = 4251,
			Campanaexpres = 4406,
			Actividaddelacampana = 4402,
			Respuestadecampana = 4401,
			Resoluciondelcaso = 4206,
			Actividaddeservicio = 4214,
			Cierredeoportunidad = 4208,
			Cierredepedido = 4209,
			Cierredeoferta = 4211,
			AlertadeCustomerVoice = 10235,
			InvitaciondeencuestadeCustomerVoice = 10245,
			RespuestadeencuestadeCustomerVoice = 10247,
			Alertadereserva = 10256,
		}

		public enum EstafacturadoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum EsunaactividadregularEnum
		{
			Si = 1,
			No = 0,
		}

		public enum EstacreadoporflujodetrabajoEnum
		{
			Si = 1,
			No = 0,
		}

		public enum PrioridadEnum
		{
			Baja = 0,
			Normal = 1,
			Alta = 2,
		}

		public enum EstadodelaactividadEnum
		{
			Abierto = 0,
			Completado = 1,
			Cancelado = 2,
		}

		public enum RazonparaelestadoEnum
		{
			Sininiciar = 2,
			Encurso = 3,
			Enesperadealguienmas = 4,
			Completado = 5,
			Cancelado = 6,
			Aplazado = 7,
		}

		#endregion

		#region Metadata

		#region Enums

		public static class Enums
		{
			/// <summary>
			/// Gets the label corresponding to the option-set's value using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="constant">The value from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns></returns>
			public static string GetLabel(string logicalName, int constant, int languageCode = 1033)
			{
				return GeneratorHelpers.GetLabel(logicalName, constant, typeof(Enums), languageCode);
			}
			/// <summary>
			/// Gets the value corresponding to the option-set's label using its logical name,
			/// the value within, and the language code.
			/// </summary>
			/// <param name="logicalName">The logical name of the option-set in CRM</param>
			/// <param name="label">The label from the option-set</param>
			/// <param name="languageCode">The language code from CRM</param>
			/// <returns>The value corresponding to the label</returns>
			public static int GetValue(string logicalName, string label, int languageCode = 1033)
			{
				return GeneratorHelpers.GetValue(logicalName, label, typeof(Enums), languageCode);
			}
		}

		#endregion

		#region Fields

		public static class Fields
		{
			#region Logical names

			public const string Parametrosadicionales = "activityadditionalparams";
			public const string TareaId = "activityid";
			public const string Tipodeactividad = "activitytypecode";
			public const string Duracion = "actualdurationminutes";
			public const string Finreal = "actualend";
			public const string Comienzoreal = "actualstart";
			public const string Categoria = "category";
			public const string Autor = "createdby";
			public const string Fechadecreacion = "createdon";
			public const string Autordelegado = "createdonbehalfby";
			public const string Idunicodetareaasignada = "crmtaskassigneduniqueid";
			public const string Descripcion = "description";
			public const string Tipodecambio = "exchangerate";
			public const string Numerodesecuenciadeimportacion = "importsequencenumber";
			public const string Estafacturado = "isbilled";
			public const string Esunaactividadregular = "isregularactivity";
			public const string Estacreadoporflujodetrabajo = "isworkflowcreated";
			public const string Ultimoperiododeretencion = "lastonholdtime";
			public const string Modificadopor = "modifiedby";
			public const string Fechademodificacion = "modifiedon";
			public const string Modificadopordelegado = "modifiedonbehalfby";
			public const string Periododeretencionminutos = "onholdtime";
			public const string Fechadecreaciondelregistro = "overriddencreatedon";
			public const string Propietario = "ownerid";
			public const string Unidaddenegociopropietaria = "owningbusinessunit";
			public const string Equipopropietario = "owningteam";
			public const string Usuariopropietario = "owninguser";
			public const string Porcentajecompletado = "percentcomplete";
			public const string Prioridad = "prioritycode";
			public const string Proceso = "processid";
			public const string Referentea = "regardingobjectid";
			public const string Duracionprogramada = "scheduleddurationminutes";
			public const string Fechadevencimiento = "scheduledend";
			public const string Fechadeinicio = "scheduledstart";
			public const string Servicio = "serviceid";
			public const string SLA = "slaid";
			public const string UltimoSLAaplicado = "slainvokedid";
			public const string Fechadeordenacion = "sortdate";
			public const string __ObsoletoFasedeproceso = "stageid";
			public const string Estadodelaactividad = "statecode";
			public const string Razonparaelestado = "statuscode";
			public const string Subcategoria = "subcategory";
			public const string Asunto = "subject";
			public const string Numerodeversionderegladezonahoraria = "timezoneruleversionnumber";
			public const string Divisa = "transactioncurrencyid";
			public const string __ObsoletoRutarecorrida = "traversedpath";
			public const string CodigodezonahorariadeconversionUTC = "utcconversiontimezonecode";
			public const string Numerodeversion = "versionnumber";

			#endregion
		}

		#endregion

		#endregion
	}

	#endregion

	#region Base code

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Class)]
	public class CrmEntityMappingAttribute : Attribute
	{
		public string LogicalName { get; private set; }
		public string SchemaName { get; private set; }
		public string DisplayName { get; private set; }

		public CrmEntityMappingAttribute(string logicalName, string schemaName, string displayName = null)
		{
			if (string.IsNullOrWhiteSpace(logicalName))
			{
				throw new ArgumentNullException("logicalName");
			}

			if (string.IsNullOrWhiteSpace(schemaName))
			{
				throw new ArgumentNullException("schemaName");
			}

			LogicalName = logicalName;
			SchemaName = schemaName;
			DisplayName = displayName;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
	public class ReadOnlyFieldAttribute : Attribute
	{ }

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
	public class CrmFieldMappingAttribute : Attribute
	{
		public string LogicalName { get; private set; }
		public string RelatedEntity { get; private set; }

		public CrmFieldMappingAttribute(string logicalName, string relatedEntity = null)
		{
			if (string.IsNullOrWhiteSpace(logicalName))
			{
				throw new ArgumentNullException("logicalName");
			}

			LogicalName = logicalName;
			RelatedEntity = relatedEntity;
		}
	}

	[DataContract]
	public enum EntityRelationRole
	{
		/// <summary>Specifies that the entity is the referencing entity. Value = 0.</summary>
		[EnumMember] Referencing,
		/// <summary>Specifies that the entity is the referenced entity. Value = 1.</summary>
		[EnumMember] Referenced,
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
	public class CrmRelationMappingAttribute : Attribute
	{
		public string SchemaName { get; private set; }
		public string RelatedEntityName { get; private set; }
		public EntityRelationRole? Role { get; private set; }

		public CrmRelationMappingAttribute(string schemaName, string relatedEntityName)
		{
			if (string.IsNullOrWhiteSpace(schemaName))
			{
				throw new ArgumentNullException("schemaName");
			}

			if (relatedEntityName == null)
			{
				throw new ArgumentNullException("relatedEntityName");
			}

			SchemaName = schemaName;
			RelatedEntityName = relatedEntityName;
		}

		public CrmRelationMappingAttribute(string schemaName, string relatedEntityName, EntityRelationRole role)
		{
			if (string.IsNullOrWhiteSpace(schemaName))
			{
				throw new ArgumentNullException("schemaName");
			}

			if (relatedEntityName == null)
			{
				throw new ArgumentNullException("relatedEntityName");
			}

			SchemaName = schemaName;
			RelatedEntityName = relatedEntityName;
			Role = role;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property)]
	public class MaxWidth : Attribute
	{
		public short Width { get; set; }

		public MaxWidth(short width)
		{
			Width = width;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property)]
	public class MaxHeight : Attribute
	{
		public short Height { get; set; }

		public MaxHeight(short height)
		{
			Height = height;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public class OptionsetLanguageLabel
	{
		public int LanguageCode { get; set; }
		public string OptionsetLabel { get; set; }
	}

	public enum ClearMode
	{
		[EnumMember] Disabled,
		[EnumMember] Empty,
		[EnumMember] Convention,
		[EnumMember] Flag
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[AttributeUsage(AttributeTargets.Property)]
	public class LabelAttribute : Attribute
	{
		public string LabelFieldNames { get; set; }
		public string LogicalName { get; set; }
		public string IdFieldName { get; set; }
		public string FieldLogicalName { get; set; }

		public LabelAttribute(string labelFieldNames, string logicalName, string idFieldName, string fieldLogicalName)
		{
			LabelFieldNames = labelFieldNames;
			LogicalName = logicalName;
			IdFieldName = idFieldName;
			FieldLogicalName = fieldLogicalName;
		}
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract]
	public class LookupValue
	{
		public LookupEntity Entity
		{
			get
			{
				LookupEntity value;
				return Enum.TryParse(EntityName, true, out value) ? value : LookupEntity.Unknown;
			}
			set => EntityName = value.ToString().ToLower();
		}

		public string EntityName
		{
			get => entityName;
			set
			{
				if (value == null) { throw new ArgumentNullException(nameof(EntityName)); }
				entityName = value;
			}
		}
		private string entityName;

		public Guid Id { get; set; }

		public LookupValue()
		{ }

		public LookupValue(string entityName, Guid id)
		{
			EntityName = entityName;
			Id = id;
		}

		public LookupValue(LookupEntity entity, Guid id)
		{
			Entity = entity;
			Id = id;
		}
	}

	public enum LookupEntity
	{
		[EnumMember] Unknown,
		[EnumMember] Account,
		[EnumMember] Contact,
		[EnumMember] User,
		[EnumMember] Team
	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	public static partial class CrmGeneratorExtensions
	{
		/// <summary>
		/// Retrieves the label of the 'OptionSetValue' using the corresponding enum member constant
		/// and the language code given.
		/// </summary>
		/// <param name="enumMember">The early-bound enum member constant; e.g.: 'Account.Enums.IndustryCode.Accounting'</param>
		/// <param name="languageCode">The language code from CRM</param>
		/// <returns>The label corresponding to the enum constant and the language code.</returns>
		public static string GetLabel(this Enum enumMember, int languageCode = 1033)
		{
			var enumType = enumMember.GetType();
			var enumName = enumType.Name.Substring(0, enumType.Name.LastIndexOf("Enum"));
			var enumParentType = enumType.DeclaringType;

			if (enumParentType == null)
			{
				return null;
			}

			var enumsType = enumParentType.GetNestedType("Enums");
			Type labelsType;

			var isContract = false;

			if (enumsType == null)
			{
				labelsType = enumParentType.Assembly.GetType($"{enumParentType.Namespace}.{enumParentType.Name}Labels", false);
				isContract = true;
			}
			else
			{
				labelsType = enumsType.GetNestedType("Labels");
			}

			if (labelsType == null)
			{
				return null;
			}

			PropertyInfo property;

			if (isContract)
			{
				property = labelsType.GetProperty($"{enumName}Enum");
			}
			else
			{
				property = labelsType.GetProperty(enumName);
			}

			if (property == null)
			{
				return null;
			}

			IDictionary<int, string> labels = null;
			IDictionary<int, IDictionary<int, string>> locLabels;

			if (isContract)
			{
				locLabels = property.GetValue(Activator.CreateInstance(labelsType)) as IDictionary<int, IDictionary<int, string>>;
			}
			else
			{
				locLabels = property.GetValue(labelsType) as IDictionary<int, IDictionary<int, string>>;
			}

			if (locLabels?.TryGetValue(languageCode, out labels) != true)
			{
				return null;
			}

			if (labels.TryGetValue((int)Enum.Parse(enumType, enumMember.ToString()), out var label) != true)
			{
				return null;
			}

			return label;
		}

	}

	[ExcludeFromCodeCoverage]
	[DebuggerNonUserCode]
	[DataContract]
	public partial class EntityContract
	{
		[DataMember] public virtual ClearMode? ValueClearMode { get { return ClearMode.Disabled; } set { } }
	}


	#endregion

}

