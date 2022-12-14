angular.module('virtoCommerce.catalogModule')
.controller('virtoCommerce.catalogModule.newCategoryWizardController', ['$scope', 'platformWebApp.bladeNavigationService', 'platformWebApp.dialogService', 'virtoCommerce.catalogModule.categories', function ($scope, bladeNavigationService, dialogService, categories) {
    var blade = $scope.blade;

    $scope.create = function () {
        blade.isLoading = true;

        blade.currentEntity.$update(null, function (data) {
            $scope.bladeClose(function () {
                var categoryListBlade = blade.parentBlade;
                categoryListBlade.setSelectedItem(data);
                categoryListBlade.showCategoryBlade(data);
                categoryListBlade.refresh();
            });
        },
        function (error) { bladeNavigationService.setError('Error ' + error.status, blade); });
    }

    $scope.openBlade = function (type) {
        blade.onClose(function () {
            var newBlade = null;
            switch (type) {
                case 'properties':
                    newBlade = {
                        id: "categoryPropertyDetail",
                        entityType: "product",
                        currentEntity: blade.currentEntity,
                        propGroups: [{ title: 'catalog.properties.category', type: 'Category' }, { title: 'catalog.properties.product', type: 'Product' }, { title: 'catalog.properties.variation', type: 'Variation' }],
                        controller: 'virtoCommerce.catalogModule.propertyListController',
                        template: 'Modules/$(VirtoCommerce.Catalog)/Scripts/blades/property-list.tpl.html'
                    };
                    break;
                case 'seo':
                    newBlade = {
                        id: "seo",
                        controller: 'virtoCommerce.coreModule.seo.storeListController',
                        template: 'Modules/$(VirtoCommerce.Core)/Scripts/SEO/blades/seo-detail.tpl.html'
                    };
                    break;
            }

            if (newBlade != null) {
                bladeNavigationService.showBlade(newBlade, blade);
            }
        });
    }

    $scope.codeValidator = function (value) {
        var pattern = /[$+;=%{}[\]|\\\/@ ~!^*&()?:'<>,]/;
        return !pattern.test(value);
    };

    $scope.setForm = function (form) {
        $scope.formScope = form;
    }
    
    blade.isLoading = false;
}]);
