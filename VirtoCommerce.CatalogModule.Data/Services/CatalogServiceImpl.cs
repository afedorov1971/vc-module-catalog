using System;
using System.Collections.Generic;
using System.Linq;
using CacheManager.Core;
using FluentValidation;
using VirtoCommerce.CatalogModule.Data.Extensions;
using VirtoCommerce.CatalogModule.Data.Model;
using VirtoCommerce.CatalogModule.Data.Repositories;
using VirtoCommerce.Domain.Catalog.Model;
using VirtoCommerce.Domain.Catalog.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Data.Common;
using VirtoCommerce.Platform.Data.Infrastructure;

namespace VirtoCommerce.CatalogModule.Data.Services
{
    public class CatalogServiceImpl : ServiceBase, ICatalogService
    {
        private readonly ICacheManager<object> _cacheManager;
        private readonly AbstractValidator<IHasProperties> _hasPropertyValidator;
        private readonly Func<ICatalogRepository> _repositoryFactory;

        public CatalogServiceImpl(Func<ICatalogRepository> catalogRepositoryFactory, ICacheManager<object> cacheManager, AbstractValidator<IHasProperties> hasPropertyValidator)
        {
            _repositoryFactory = catalogRepositoryFactory;
            _cacheManager = cacheManager;
            _hasPropertyValidator = hasPropertyValidator;
        }

        #region ICatalogService Members

        public Catalog GetById(string catalogId)
        {
            Catalog result;
            if (PreloadCatalogs().TryGetValue(catalogId, out result))
            {
                //Clone required because client code may change resulting objects
                result = MemberwiseCloneCatalog(result);
            }
            return result;
        }

        public Catalog Create(Catalog catalog)
        {
            SaveChanges(new[] { catalog });
            var result = GetById(catalog.Id);
            return result;
        }

        public void Update(Catalog[] catalogs)
        {
            SaveChanges(catalogs);
        }

        public void Delete(string[] catalogIds)
        {
            using (var repository = _repositoryFactory())
            {
                repository.RemoveCatalogs(catalogIds);
                CommitChanges(repository);
                //Reset cached catalogs and catalogs
                ResetCache();
            }
        }

        public IEnumerable<Catalog> GetCatalogsList()
        {
            //Clone required because client code may change resulting objects
            return PreloadCatalogs().Values.Select(MemberwiseCloneCatalog).OrderBy(x => x.Name);
        }

        #endregion

        protected virtual void SaveChanges(Catalog[] catalogs)
        {
            var pkMap = new PrimaryKeyResolvingMap();

            using (var repository = _repositoryFactory())
            using (var changeTracker = GetChangeTracker(repository))
            {
                ValidateCatalogProperties(catalogs);
                var dbExistEntities = repository.GetCatalogsByIds(catalogs.Where(x => !x.IsTransient()).Select(x => x.Id).ToArray());
                foreach (var catalog in catalogs)
                {
                    var originalEntity = dbExistEntities.FirstOrDefault(x => x.Id == catalog.Id);
                    var modifiedEntity = AbstractTypeFactory<CatalogEntity>.TryCreateInstance().FromModel(catalog, pkMap);
                    if (originalEntity != null)
                    {
                        changeTracker.Attach(originalEntity);
                        modifiedEntity.Patch(originalEntity);
                    }
                    else
                    {
                        repository.Add(modifiedEntity);
                    }
                }
                CommitChanges(repository);
                pkMap.ResolvePrimaryKeys();
                //Reset cached catalogs and catalogs
                ResetCache();
            }
        }

        protected virtual void ResetCache()
        {
            _cacheManager.ClearRegion(CatalogConstants.CacheRegion);
        }


        protected virtual Catalog MemberwiseCloneCatalog(Catalog catalog)
        {
            var retVal = AbstractTypeFactory<Catalog>.TryCreateInstance();

            retVal.Id = catalog.Id;
            retVal.IsVirtual = catalog.IsVirtual;
            retVal.Name = catalog.Name;

            // TODO: clone reference objects
            retVal.Languages = catalog.Languages;
            retVal.Properties = catalog.Properties;
            retVal.PropertyValues = catalog.PropertyValues;

            return retVal;
        }

        protected virtual Dictionary<string, Catalog> PreloadCatalogs()
        {
            return _cacheManager.Get("AllCatalogs", CatalogConstants.CacheRegion, () =>
            {
                CatalogEntity[] entities;
                using (var repository = _repositoryFactory())
                {
                    //Optimize performance and CPU usage
                    repository.DisableChangesTracking();

                    entities = repository.GetCatalogsByIds(repository.Catalogs.Select(x => x.Id).ToArray());
                }

                var result = entities.Select(x => x.ToModel(AbstractTypeFactory<Catalog>.TryCreateInstance())).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

                LoadDependencies(result.Values, result);
                return result;
            });
        }

        protected virtual void LoadDependencies(IEnumerable<Catalog> catalogs, Dictionary<string, Catalog> preloadedCatalogsMap)
        {
            if (catalogs == null)
            {
                throw new ArgumentNullException(nameof(catalogs));
            }
            foreach (var catalog in catalogs.Where(x => !x.IsTransient()))
            {
                Catalog preloadedCatalog;
                if (preloadedCatalogsMap.TryGetValue(catalog.Id, out preloadedCatalog))
                {
                    catalog.Properties = preloadedCatalog.Properties;
                    foreach (var property in catalog.Properties)
                    {
                        property.Catalog = preloadedCatalogsMap[property.CatalogId];
                    }
                    if (!catalog.PropertyValues.IsNullOrEmpty())
                    {
                        //Next need set Property in PropertyValues objects
                        foreach (var propValue in catalog.PropertyValues.ToArray())
                        {
                            propValue.Property = catalog.Properties.Where(x => x.Type == PropertyType.Catalog)
                                                                   .FirstOrDefault(x => x.IsSuitableForValue(propValue));
                            //Because multilingual dictionary values for all languages may not stored in db then need to add it in result manually from property dictionary values
                            var localizedDictValues = propValue.TryGetAllLocalizedDictValues();
                            foreach (var localizedDictValue in localizedDictValues)
                            {
                                if (!catalog.PropertyValues.Any(x => x.ValueId == localizedDictValue.ValueId && x.LanguageCode == localizedDictValue.LanguageCode))
                                {
                                    catalog.PropertyValues.Add(localizedDictValue);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ValidateCatalogProperties(Catalog[] catalogs)
        {
            LoadDependencies(catalogs, PreloadCatalogs());
            foreach (var catalog in catalogs)
            {
                var validatioResult = _hasPropertyValidator.Validate(catalog);
                if (!validatioResult.IsValid)
                    throw new Exception($"Catalog properties has validation error: {string.Join(Environment.NewLine, validatioResult.Errors.Select(x => x.ToString()))}");
            }
        }
    }
}
