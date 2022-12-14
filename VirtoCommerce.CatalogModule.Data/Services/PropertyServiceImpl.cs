using CacheManager.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.CatalogModule.Data.Extensions;
using VirtoCommerce.CatalogModule.Data.Model;
using VirtoCommerce.CatalogModule.Data.Repositories;
using VirtoCommerce.Domain.Catalog.Model;
using VirtoCommerce.Domain.Catalog.Services;
using VirtoCommerce.Domain.Search;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Data.Common;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.CatalogModule.Data.Services
{
    public class PropertyServiceImpl : ServiceBase, IPropertyService
    {
        private readonly Func<ICatalogRepository> _repositoryFactory;
        private readonly ICacheManager<object> _cacheManager;
        private readonly ICatalogService _catalogService;

        public PropertyServiceImpl(Func<ICatalogRepository> repositoryFactory, ICacheManager<object> cacheManager, ICatalogService catalogService)
        {
            _repositoryFactory = repositoryFactory;
            _cacheManager = cacheManager;
            _catalogService = catalogService;
        }

        #region IPropertyService Members

        public Property GetById(string propertyId)
        {
            return GetByIds(new[] { propertyId }).FirstOrDefault();
        }

        public Property[] GetByIds(string[] propertyIds)
        {
            var preloadedProperties = PreloadAllProperties();

            var result = propertyIds
                .Where(propertyId => preloadedProperties.ContainsKey(propertyId))
                .Select(propertyId => preloadedProperties[propertyId])
                .Select(MemberwiseCloneProperty)
                .ToArray();

            return result;
        }

        public Property[] GetAllCatalogProperties(string catalogId)
        {
            var preloadedProperties = PreloadAllCatalogProperties(catalogId);
            var result = preloadedProperties.Select(MemberwiseCloneProperty).ToArray();
            return result;
        }


        public Property[] GetAllProperties()
        {
            var preloadedProperties = PreloadAllProperties();
            var result = preloadedProperties.Values.Select(MemberwiseCloneProperty).ToArray();
            return result;
        }


        public Property Create(Property property)
        {
            if (property.CatalogId == null)
            {
                throw new NullReferenceException("property.CatalogId");
            }

            SaveChanges(new[] { property });

            var result = GetById(property.Id);
            return result;
        }

        public void Update(Property[] properties)
        {
            SaveChanges(properties);
        }


        public void Delete(string[] propertyIds)
        {
            using (var repository = _repositoryFactory())
            {
                var entities = repository.GetPropertiesByIds(propertyIds);

                foreach (var entity in entities)
                {
                    repository.Remove(entity);
                }

                CommitChanges(repository);
                //Reset cached categories and catalogs
                ResetCache();
            }
        }

        public PropertyDictionaryValue[] SearchDictionaryValues(string propertyId, string keyword)
        {
            var property = GetById(propertyId);
            var result = property.DictionaryValues.ToArray();
            if (!string.IsNullOrEmpty(keyword))
            {
                result = result.Where(x => x.Value.Contains(keyword)).ToArray();
            }
            return result;
        }
        #endregion

        protected virtual void LoadDependencies(Property[] properties)
        {
            var catalogsMap = _catalogService.GetCatalogsList().ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                property.Catalog = catalogsMap.GetValueOrThrow(property.CatalogId, $"property catalog with key {property.CatalogId} not exist");
            }
        }

        protected virtual void ApplyInheritanceRules(Property[] properties)
        {

            foreach (var property in properties)
            {
                var displayNamesComparer = AnonymousComparer.Create((PropertyDisplayName x) => $"{x.LanguageCode}");
                var displayNamesForCatalogLanguages = property.Catalog.Languages.Select(x => new PropertyDisplayName { LanguageCode = x.LanguageCode }).ToList();

                //Leave display names only with catalog languages
                property.DisplayNames = property.DisplayNames.Intersect(displayNamesForCatalogLanguages, displayNamesComparer).ToList();

                //Add missed
                property.DisplayNames.AddRange(displayNamesForCatalogLanguages.Except(property.DisplayNames, displayNamesComparer));
            }
        }

        protected virtual void TryAddPredefinedValidationRules(Property[] properties)
        {
            foreach (var property in properties)
            { 
                if(property.ValueType == PropertyValueType.GeoPoint)
                {
                    var geoPointValidationRule = property.ValidationRules?.FirstOrDefault(x => x.RegExp.EqualsInvariant(GeoPoint.Regexp.ToString()));
                    if(geoPointValidationRule == null)
                    {
                        if(property.ValidationRules == null)
                        {
                            property.ValidationRules = new List<PropertyValidationRule>();
                        }
                        property.ValidationRules.Add(new PropertyValidationRule { RegExp = GeoPoint.Regexp.ToString() });
                    }
                }
            }
        }

        protected virtual void SaveChanges(Property[] properties)
        {
            var pkMap = new PrimaryKeyResolvingMap();

            using (var repository = _repositoryFactory())
            using (var changeTracker = GetChangeTracker(repository))
            {
                TryAddPredefinedValidationRules(properties);

                var dbExistEntities = repository.GetPropertiesByIds(properties.Where(x => !x.IsTransient()).Select(x => x.Id).ToArray());
                foreach (var property in properties)
                {
                    var originalEntity = dbExistEntities.FirstOrDefault(x => x.Id == property.Id);
                    var modifiedEntity = AbstractTypeFactory<PropertyEntity>.TryCreateInstance().FromModel(property, pkMap);
                    if (originalEntity != null)
                    {
                        changeTracker.Attach(originalEntity);
                        modifiedEntity.Patch(originalEntity);
                        //Force set ModifiedDate property to mark a property changed. Special for  partial update cases when property table not have changes
                        originalEntity.ModifiedDate = DateTime.UtcNow;
                    }
                    else
                    {
                        repository.Add(modifiedEntity);
                    }
                }
                CommitChanges(repository);
                pkMap.ResolvePrimaryKeys();
                //Reset cached categories and catalogs
                ResetCache();
            }
        }

        protected virtual void ResetCache()
        {
            _cacheManager.ClearRegion(CatalogConstants.CacheRegion);
        }

        protected virtual IDictionary<string, Property> PreloadAllProperties()
        {
            return _cacheManager.Get("AllProperties", CatalogConstants.CacheRegion, () =>
            {
                using (var repository = _repositoryFactory())
                {
                    repository.DisableChangesTracking();

                    var propertyIds = repository.Properties.Select(p => p.Id).ToArray();
                    var entities = repository.GetPropertiesByIds(propertyIds);
                    var properties = entities.Select(p => p.ToModel(AbstractTypeFactory<Property>.TryCreateInstance())).ToArray();

                    LoadDependencies(properties);
                    ApplyInheritanceRules(properties);

                    var result = properties.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

                    return result;
                }
            });
        }

        protected virtual IList<Property> PreloadAllCatalogProperties(string catalogId)
        {
            return _cacheManager.Get($"AllCatalogProperties-{catalogId}", CatalogConstants.CacheRegion, () =>
            {
                using (var repository = _repositoryFactory())
                {
                    repository.DisableChangesTracking();

                    var result = repository.GetAllCatalogProperties(catalogId)
                        .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase) // Remove duplicates
                        .Select(g => g.First())
                        .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(p => p.ToModel(AbstractTypeFactory<Property>.TryCreateInstance()))
                        .ToArray();

                    return result;
                }
            });
        }

        // TODO: Move to domain
        protected virtual Property MemberwiseCloneProperty(Property property)
        {
            var result = AbstractTypeFactory<Property>.TryCreateInstance();

            // Entity
            result.Id = property.Id;

            // AuditableEntity
            result.CreatedDate = property.CreatedDate;
            result.ModifiedDate = property.ModifiedDate;
            result.CreatedBy = property.CreatedBy;
            result.ModifiedBy = property.ModifiedBy;

            // Property
            result.CatalogId = property.CatalogId;
            result.CategoryId = property.CategoryId;
            result.Name = property.Name;
            result.Required = property.Required;
            result.Dictionary = property.Dictionary;
            result.Multivalue = property.Multivalue;
            result.Multilanguage = property.Multilanguage;
            result.Type = property.Type;
            result.ValueType = property.ValueType;
            result.IsInherited = property.IsInherited;

            // TODO: clone reference objects
            result.Catalog = property.Catalog;
            result.Category = property.Category;
            result.Attributes = property.Attributes;
            result.DictionaryValues = property.DictionaryValues;
            result.DisplayNames = property.DisplayNames;
            result.ValidationRules = property.ValidationRules;

            return result;
        }
    }
}
